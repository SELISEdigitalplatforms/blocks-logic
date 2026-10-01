using Blocks.Genesis;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// Consumes <c>functions:build-results</c> and applies them to the tenant's
    /// <c>FunctionBuilds</c> collection, which is what <see cref="Services.FunctionBuildService"/>
    /// polls. Much simpler than <see cref="FunctionResultConsumer"/>: a build has no output
    /// actions and no retry-on-failure — a failed build simply reports its error to whoever
    /// is waiting on it (a Test or Deploy call), and the developer decides whether to retry.
    /// </summary>
    public sealed class FunctionBuildResultConsumer : BackgroundService
    {
        private const int MaxConcurrentBuildResults = 5;
        private const int MaxDeliveryAttempts = 5;
        private static readonly TimeSpan ReclaimIdle = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ReclaimInterval = TimeSpan.FromSeconds(30);

        private readonly IDatabase _db;
        private readonly IFunctionBuildRepository _buildRepository;
        private readonly ILogger<FunctionBuildResultConsumer> _logger;

        public FunctionBuildResultConsumer(
            ICacheClient cache, IFunctionBuildRepository buildRepository, ILogger<FunctionBuildResultConsumer> logger)
        {
            _db = cache.CacheDatabase();
            _buildRepository = buildRepository;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumerName = $"{Environment.MachineName}-{Environment.ProcessId}";
            var consumer = new FunctionResultGroupConsumer(
                _db, _logger, FunctionQueueKeys.BuildResultsStream, consumerName);
            await consumer.EnsureGroupAsync();

            using var gate = new SemaphoreSlim(MaxConcurrentBuildResults);
            var nextReclaim = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Consuming {Stream} as {Consumer}", FunctionQueueKeys.BuildResultsStream, consumerName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var entries = await consumer.ReadNewAsync(count: MaxConcurrentBuildResults);

                    if (entries.Count == 0 && DateTimeOffset.UtcNow >= nextReclaim)
                    {
                        nextReclaim = DateTimeOffset.UtcNow.Add(ReclaimInterval);
                        entries = await consumer.ReclaimAbandonedAsync(ReclaimIdle, count: MaxConcurrentBuildResults);
                    }

                    if (entries.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await gate.WaitAsync(stoppingToken);
                        _ = HandleAsync(consumer, entry, gate, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The Functions build-result loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }

        private async Task HandleAsync(
            FunctionResultGroupConsumer consumer, ResultStreamEntry entry, SemaphoreSlim gate, CancellationToken stoppingToken)
        {
            try
            {
                await ProcessAsync(entry, stoppingToken);
                await consumer.AcknowledgeAsync(entry.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process build-result entry {Id}", entry.Id);
                var deliveries = await consumer.DeliveryCountAsync(entry.Id);
                if (deliveries >= MaxDeliveryAttempts)
                {
                    await consumer.DeadLetterAsync(entry, $"delivered {deliveries} times without completing: {ex.Message}");
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task ProcessAsync(ResultStreamEntry entry, CancellationToken cancellationToken)
        {
            var buildId = entry.Get("buildId");
            var tenantId = entry.Get("tenantId");

            if (string.IsNullOrWhiteSpace(buildId) || string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogError(
                    "Dropping a build-result entry with no buildId/tenantId (buildId={BuildId}, tenantId={TenantId})",
                    buildId, tenantId);
                return;
            }

            var status = string.Equals(entry.Get("status"), "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                ? BuildStatus.Succeeded
                : BuildStatus.Failed;

            await _buildRepository.ApplyResultAsync(
                tenantId, buildId, status,
                entry.Get("imageDigest"), entry.Get("packages"), entry.Get("log"), entry.Get("errorMessage"),
                DateTime.UtcNow, cancellationToken);
        }
    }
}
