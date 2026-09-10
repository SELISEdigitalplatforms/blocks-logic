using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// Sweeps <c>functions:retries</c> (a sorted set scored by due epoch-second) and re-enqueues
    /// a failed run's next attempt once its backoff delay has elapsed —
    /// <see cref="FunctionResultConsumer"/> is what schedules an entry here, on a retryable
    /// failure with attempts remaining.
    /// <para>
    /// A retry reruns the <b>exact envelope</b> the original attempt built — same input, same
    /// caller identity, same non-secret configuration — with only <c>run.attempt</c> patched to
    /// the new number, read back from <c>function:run:{runId}</c> (which is still inside its
    /// 24 h TTL for any retry delay this short). This is why retries only apply to a run with a
    /// deployed version behind it: a Test run's envelope has nowhere durable to be reproduced
    /// from, and re-testing manually is one click away regardless.
    /// </para>
    /// <para>
    /// The ZREM before acting is the claim: two Worker instances racing the same due entry will
    /// have exactly one of them successfully remove it, so only one re-enqueues it.
    /// </para>
    /// </summary>
    public sealed class FunctionRetryScheduler : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);
        private const int BatchSize = 50;

        private readonly IDatabase _db;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly ILogger<FunctionRetryScheduler> _logger;

        public FunctionRetryScheduler(
            ICacheClient cache,
            IFunctionRunRepository runRepository,
            IFunctionVersionRepository versionRepository,
            ILogger<FunctionRetryScheduler> logger)
        {
            _db = cache.CacheDatabase();
            _runRepository = runRepository;
            _versionRepository = versionRepository;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "The Functions retry sweep hit an unexpected error");
                }

                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        internal async Task SweepOnceAsync(CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var due = await _db.SortedSetRangeByScoreAsync(
                FunctionQueueKeys.RetryQueue, double.NegativeInfinity, now, take: BatchSize);

            foreach (var member in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The claim: only the instance whose ZREM actually removes something proceeds.
                var removed = await _db.SortedSetRemoveAsync(FunctionQueueKeys.RetryQueue, member);
                if (!removed) continue;

                await ProcessDueEntryAsync(member!, cancellationToken);
            }
        }

        private async Task ProcessDueEntryAsync(string member, CancellationToken cancellationToken)
        {
            RetryEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<RetryEntry>(member);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Could not parse a retry-queue entry: {Member}", member);
                return;
            }

            if (entry is null || string.IsNullOrEmpty(entry.RunId) || string.IsNullOrEmpty(entry.TenantId))
            {
                _logger.LogError("A retry-queue entry was missing required fields: {Member}", member);
                return;
            }

            var run = await _runRepository.GetByIdAsync(entry.TenantId, entry.RunId, cancellationToken);
            if (run is null)
            {
                _logger.LogWarning("Retry for run {RunId} skipped: the run no longer exists", entry.RunId);
                return;
            }
            if (run.Attempt != entry.Attempt - 1)
            {
                // Stale: something else already advanced (or reset) this run's attempt since
                // this retry was scheduled. Acting on it now would replay an attempt out of
                // order.
                _logger.LogInformation(
                    "Retry for run {RunId} skipped: expected prior attempt {Expected}, found {Actual}",
                    entry.RunId, entry.Attempt - 1, run.Attempt);
                return;
            }

            var version = string.IsNullOrEmpty(entry.VersionId)
                ? null
                : await _versionRepository.GetByIdAsync(entry.TenantId, entry.VersionId, cancellationToken);
            if (version is null || string.IsNullOrEmpty(version.ImageDigest))
            {
                _logger.LogWarning(
                    "Retry for run {RunId} skipped: version '{VersionId}' is no longer available", entry.RunId, entry.VersionId);
                return;
            }

            var runKey = FunctionQueueKeys.Run(entry.RunId);
            var envelopeJson = await _db.HashGetAsync(runKey, "envelope");
            if (envelopeJson.IsNullOrEmpty)
            {
                _logger.LogWarning(
                    "Retry for run {RunId} skipped: its execution envelope has expired", entry.RunId);
                return;
            }

            string patchedEnvelope;
            try
            {
                var node = JsonNode.Parse((string)envelopeJson!)!.AsObject();
                node["run"]!["attempt"] = entry.Attempt;
                patchedEnvelope = node.ToJsonString();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException)
            {
                _logger.LogError(ex, "Could not patch the execution envelope for run {RunId}'s retry", entry.RunId);
                return;
            }

            await _runRepository.ResetForRetryAsync(entry.TenantId, entry.RunId, entry.Attempt, cancellationToken);

            await _db.HashSetAsync(runKey,
            [
                new HashEntry("envelope", patchedEnvelope),
                new HashEntry("status", FunctionQueueKeys.Wire.Queued),
            ]);
            await _db.KeyExpireAsync(runKey, FunctionQueueKeys.RunTtl);

            await _db.StreamAddAsync(FunctionQueueKeys.RunsStream,
            [
                new NameValueEntry("runId", entry.RunId),
                new NameValueEntry("functionId", entry.FunctionId),
                new NameValueEntry("versionId", entry.VersionId ?? string.Empty),
                new NameValueEntry("tenantId", entry.TenantId),
                new NameValueEntry("image", version.ImageDigest),
                new NameValueEntry("attempt", entry.Attempt),
                new NameValueEntry("protocol", FunctionQueueKeys.ProtocolVersion),
            ]);

            _logger.LogInformation("Re-enqueued run {RunId} for attempt {Attempt}", entry.RunId, entry.Attempt);
        }

        private sealed class RetryEntry
        {
            public string? RunId { get; set; }
            public string? TenantId { get; set; }
            public string? FunctionId { get; set; }
            public string? VersionId { get; set; }
            public int Attempt { get; set; }
        }
    }
}
