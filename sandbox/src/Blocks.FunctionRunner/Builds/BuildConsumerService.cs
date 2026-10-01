using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Health;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Builds
{
    /// <summary>
    /// Consumes build jobs.
    /// <para>
    /// Builds have their own budget so a queue of them cannot starve executions: at most one
    /// build runs at a time on a host, because a build is far heavier than a run and the two
    /// compete for the same CPU.
    /// </para>
    /// </summary>
    public sealed class BuildConsumerService : BackgroundService
    {
        private readonly IDatabase _db;
        private readonly BuildProcessor _processor;
        private readonly HeartbeatService _heartbeat;
        private readonly RunnerOptions _options;
        private readonly ILogger<BuildConsumerService> _logger;

        public BuildConsumerService(
            IDatabase db,
            BuildProcessor processor,
            HeartbeatService heartbeat,
            IOptions<RunnerOptions> options,
            ILogger<BuildConsumerService> logger)
        {
            _db = db;
            _processor = processor;
            _heartbeat = heartbeat;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.ProcessBuilds)
            {
                _logger.LogInformation("Build processing is disabled on this host");
                return;
            }

            var consumer = new GroupConsumer(
                _db, _logger, RedisKeys.BuildsStream, RedisKeys.RunnerGroup, _options.RunnerId);
            await consumer.EnsureGroupAsync().ConfigureAwait(false);

            _logger.LogInformation("Consuming {Stream} as {Consumer}", RedisKeys.BuildsStream, _options.RunnerId);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_heartbeat.Latest is { Healthy: false })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    var entries = await consumer.ReadNewAsync(count: 1, stoppingToken).ConfigureAwait(false);
                    if (entries.Count == 0)
                    {
                        entries = await consumer.ReclaimAbandonedAsync(
                            TimeSpan.FromSeconds(_options.BuildTimeoutSeconds * 2), count: 1).ConfigureAwait(false);
                    }
                    if (entries.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        await HandleAsync(consumer, entry, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The build loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("Build loop stopped");
        }

        private async Task HandleAsync(GroupConsumer consumer, ClaimedEntry entry, CancellationToken token)
        {
            var buildId = entry.Get("buildId");
            if (string.IsNullOrWhiteSpace(buildId))
            {
                await consumer.DeadLetterAsync(entry, "the entry carries no buildId").ConfigureAwait(false);
                return;
            }

            var deliveries = await consumer.DeliveryCountAsync(entry.Id).ConfigureAwait(false);
            if (deliveries > _options.MaxAttempts)
            {
                await consumer.DeadLetterAsync(entry, $"delivered {deliveries} times without completing")
                    .ConfigureAwait(false);
                return;
            }

            var job = new BuildJob
            {
                BuildId = buildId,
                FunctionId = entry.Get("functionId") ?? buildId,
                TenantId = entry.Get("tenantId"),
                SourceKey = entry.Get("sourceKey") ?? RedisKeys.Source(buildId),
                ImageRef = entry.Get("imageRef") ?? string.Empty,
                AllowScripts = string.Equals(entry.Get("allowScripts"), "true", StringComparison.OrdinalIgnoreCase),
            };

            if (string.IsNullOrWhiteSpace(job.ImageRef))
            {
                await consumer.DeadLetterAsync(entry, "the entry carries no imageRef").ConfigureAwait(false);
                return;
            }

            await _processor.ProcessAsync(job, token).ConfigureAwait(false);

            // The build result is already on its stream, so acknowledging now is safe.
            await consumer.AcknowledgeAsync(entry.Id).ConfigureAwait(false);
        }
    }
}
