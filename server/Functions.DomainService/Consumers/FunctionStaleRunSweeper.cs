using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// Closes runs that will never hear back — the net under every way a run can be lost
    /// between "written to Mongo" and "result applied".
    /// <para>
    /// The ways it happens: the queued payload (<c>function:run:{runId}</c>) outlives
    /// <see cref="FunctionQueueKeys.RunTtl"/> before any runner reaches it, and the runner then
    /// discards the stream entry as having nothing to run; Redis evicts or loses the payload
    /// earlier than that; a runner takes a run and dies without reporting; a result entry
    /// outlives the 12 h stream retention unread; an output job is lost. None of those ever
    /// produces a result, so without this the record says QUEUED (or OUTPUT_PROCESSING) for ever.
    /// </para>
    /// <para>
    /// Deadlines, each measured from the record's <c>LastUpdatedDate</c> (the moment it entered
    /// its current state — a retry re-queue moves it):
    /// <list type="bullet">
    /// <item><b>QUEUED</b>: <c>RunTtl</c> + the longest possible execution + margin — past that,
    /// no runner can still be holding it. Earlier, after <c>Functions:StaleRunMissingPayloadMinutes</c>,
    /// if the payload is already gone from Redis, since then nothing can ever run it.
    /// FAILED / <see cref="RunErrorCode.Abandoned"/>.</item>
    /// <item><b>CLAIMED / STARTING / RUNNING</b>: the longest possible execution + grace +
    /// margin. TIMED_OUT / <see cref="RunErrorCode.Abandoned"/>.</item>
    /// <item><b>OUTPUT_PROCESSING</b>: <c>Functions:StaleOutputProcessingMinutes</c>.
    /// OUTPUT_FAILED / <see cref="RunErrorCode.OutputActionFailed"/>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Never overwrites a real outcome.</b> Every close is a compare-and-set on the status,
    /// attempt and <c>LastUpdatedDate</c> the sweep read (<see cref="IFunctionRunRepository.CloseStaleAsync"/>),
    /// so a result that lands between the read and the write wins. And because
    /// <see cref="RunErrorCode.Abandoned"/> is a platform-determined outcome, a real result for the
    /// same attempt that arrives even later still replaces it. No retry is ever scheduled from here.
    /// </para>
    /// <para>
    /// Visits every enabled tenant (<see cref="IFunctionTenantSource"/>), one at a time; one
    /// tenant's failure never stops the others. A Redis lock keeps several Worker instances from
    /// all sweeping on the same tick, but correctness does not depend on it.
    /// </para>
    /// </summary>
    public sealed class FunctionStaleRunSweeper : BackgroundService
    {
        internal const int DefaultIntervalSeconds = 300;
        internal const int DefaultMarginSeconds = 600;
        internal const int DefaultMissingPayloadMinutes = 15;
        internal const int DefaultOutputProcessingMinutes = 360;
        internal const int DefaultBatchSize = 500;

        /// <summary>Allowance between a run's timeout firing and its result being on the stream.</summary>
        internal const int RunningGraceSeconds = 30;

        private static readonly RunStatus[] QueuedStatuses = [RunStatus.Queued];
        private static readonly RunStatus[] ExecutingStatuses = [RunStatus.Claimed, RunStatus.Starting, RunStatus.Running];
        private static readonly RunStatus[] OutputStatuses = [RunStatus.OutputProcessing];

        private readonly IDatabase _db;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionTenantSource _tenantSource;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FunctionStaleRunSweeper> _logger;
        private readonly string _owner = $"{Environment.MachineName}-{Environment.ProcessId}";

        public FunctionStaleRunSweeper(
            ICacheClient cache,
            IFunctionRunRepository runRepository,
            IFunctionTenantSource tenantSource,
            IConfiguration configuration,
            ILogger<FunctionStaleRunSweeper> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            _db = cache.CacheDatabase();
            _runRepository = runRepository;
            _tenantSource = tenantSource;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>The effective settings, resolved from configuration with floors that keep them safe.</summary>
        internal sealed record SweepSettings(
            TimeSpan Interval,
            TimeSpan QueuedDeadline,
            TimeSpan MissingPayloadDeadline,
            TimeSpan ExecutingDeadline,
            TimeSpan OutputDeadline,
            int BatchSize);

        internal SweepSettings ResolveSettings()
        {
            var interval = TimeSpan.FromSeconds(Math.Max(30,
                _configuration.GetValue("Functions:StaleRunSweepIntervalSeconds", DefaultIntervalSeconds)));
            var margin = TimeSpan.FromSeconds(Math.Max(60,
                _configuration.GetValue("Functions:StaleRunMarginSeconds", DefaultMarginSeconds)));
            var longestExecution = TimeSpan.FromSeconds(FunctionLimits.Ceiling.TimeoutSeconds + RunningGraceSeconds);

            // Floored, not merely defaulted: below these, a configuration value would close runs
            // that are still legitimately waiting — the one thing this sweep must never do.
            var missingPayload = TimeSpan.FromMinutes(Math.Max(5,
                _configuration.GetValue("Functions:StaleRunMissingPayloadMinutes", DefaultMissingPayloadMinutes)));
            var output = TimeSpan.FromMinutes(Math.Max(30,
                _configuration.GetValue("Functions:StaleOutputProcessingMinutes", DefaultOutputProcessingMinutes)));
            var batch = Math.Clamp(
                _configuration.GetValue("Functions:StaleRunSweepBatchSize", DefaultBatchSize), 1, 5000);

            return new SweepSettings(
                interval,
                QueuedDeadline: FunctionQueueKeys.RunTtl + longestExecution + margin,
                MissingPayloadDeadline: missingPayload,
                ExecutingDeadline: longestExecution + margin,
                OutputDeadline: output,
                BatchSize: batch);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Functions:StaleRunSweepEnabled", true))
            {
                _logger.LogWarning("The stale-run sweep is switched off (Functions:StaleRunSweepEnabled=false)");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = ResolveSettings();
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Housekeeping: never take the Worker down over it.
                    _logger.LogError(ex, "The stale-run sweep failed; will retry on the next tick");
                }

                try
                {
                    await Task.Delay(settings.Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>One pass over every tenant. Returns how many runs it closed.</summary>
        internal async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
        {
            var settings = ResolveSettings();

            if (!await TryTakeLockAsync(settings.Interval))
            {
                _logger.LogDebug("Another Worker holds the stale-run sweep lock; skipping this tick");
                return 0;
            }

            IReadOnlyList<string> tenants;
            try
            {
                tenants = await _tenantSource.GetActiveTenantIdsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "The stale-run sweep could not list tenants");
                return 0;
            }

            var closed = 0;
            foreach (var tenantId in tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    closed += await SweepTenantAsync(tenantId, settings, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "The stale-run sweep failed for tenant {TenantId}; continuing with the rest", tenantId);
                }
            }

            if (closed > 0)
            {
                _logger.LogWarning("The stale-run sweep closed {Count} run(s) across {Tenants} tenant(s)", closed, tenants.Count);
            }
            return closed;
        }

        private async Task<bool> TryTakeLockAsync(TimeSpan interval)
        {
            try
            {
                // Held a little less than the interval so the next tick is never locked out by
                // its own previous pass.
                var hold = interval - TimeSpan.FromSeconds(Math.Min(10, interval.TotalSeconds / 10));
                return await _db.StringSetAsync(FunctionWorkerQueueKeys.StaleSweepLock, _owner, hold, When.NotExists);
            }
            catch (Exception ex)
            {
                // The lock only saves duplicate work. Without Redis, sweep anyway: every write is
                // conditional, and a Redis outage is exactly when runs get stranded.
                _logger.LogWarning("Could not take the stale-run sweep lock ({Message}); sweeping anyway", ex.Message);
                return true;
            }
        }

        private async Task<int> SweepTenantAsync(string tenantId, SweepSettings settings, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var closed = 0;

            // QUEUED past every deadline: no runner can be holding it any longer.
            var queuedCutoff = now - settings.QueuedDeadline;
            foreach (var run in await _runRepository.FindStaleCandidatesAsync(
                         tenantId, QueuedStatuses, queuedCutoff, updatedNotBefore: null, settings.BatchSize, cancellationToken))
            {
                closed += await CloseAsync(tenantId, run, RunStatus.Failed, RunErrorCode.Abandoned,
                    $"the run was never picked up: no outcome was reported within {settings.QueuedDeadline.TotalHours:0.#}h of it " +
                    "being queued and its queued payload has expired. Nothing is known to have executed; replay it to run it again.",
                    cancellationToken);
            }

            // QUEUED for a while, and the payload a runner would need is already gone.
            var missingCutoff = now - settings.MissingPayloadDeadline;
            foreach (var run in await _runRepository.FindStaleCandidatesAsync(
                         tenantId, QueuedStatuses, missingCutoff, updatedNotBefore: queuedCutoff, settings.BatchSize, cancellationToken))
            {
                bool payloadExists;
                try
                {
                    payloadExists = await _db.KeyExistsAsync(FunctionQueueKeys.Run(run.ItemId));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Not knowing is not the same as "missing": leave it for the hard deadline.
                    _logger.LogWarning("Could not check the payload of run {RunId}: {Message}", run.ItemId, ex.Message);
                    continue;
                }
                if (payloadExists) continue;

                closed += await CloseAsync(tenantId, run, RunStatus.Failed, RunErrorCode.Abandoned,
                    "the run's queued payload is no longer in Redis (expired, evicted or flushed), so no runner can execute it. " +
                    "Nothing is known to have executed; replay it to run it again.",
                    cancellationToken);
            }

            // Taken by a runner that never answered.
            var executingCutoff = now - settings.ExecutingDeadline;
            foreach (var run in await _runRepository.FindStaleCandidatesAsync(
                         tenantId, ExecutingStatuses, executingCutoff, updatedNotBefore: null, settings.BatchSize, cancellationToken))
            {
                closed += await CloseAsync(tenantId, run, RunStatus.TimedOut, RunErrorCode.Abandoned,
                    $"no result was reported within {settings.ExecutingDeadline.TotalSeconds:0}s of the run starting — the runner " +
                    "executing it most likely stopped. Its outcome is unknown.",
                    cancellationToken);
            }

            // Output delivery that never finished.
            var outputCutoff = now - settings.OutputDeadline;
            foreach (var run in await _runRepository.FindStaleCandidatesAsync(
                         tenantId, OutputStatuses, outputCutoff, updatedNotBefore: null, settings.BatchSize, cancellationToken))
            {
                closed += await CloseAsync(tenantId, run, RunStatus.OutputFailed, RunErrorCode.OutputActionFailed,
                    $"output delivery did not finish within {settings.OutputDeadline.TotalHours:0.#}h; some output actions may " +
                    "have been delivered. The function itself succeeded.",
                    cancellationToken);
            }

            return closed;
        }

        private async Task<int> CloseAsync(
            string tenantId, FunctionRunEntity run, RunStatus newStatus, RunErrorCode errorCode, string message,
            CancellationToken cancellationToken)
        {
            var closed = await _runRepository.CloseStaleAsync(
                tenantId, run.ItemId, run.Status, run.Attempt, run.LastUpdatedDate,
                newStatus, errorCode, message, DateTime.UtcNow, cancellationToken);

            if (!closed)
            {
                // Something moved it on between the read and the write — a result, a retry reset,
                // output delivery finishing. That outcome stands.
                _logger.LogDebug("Run {RunId} changed while being swept; leaving it", run.ItemId);
                return 0;
            }

            _logger.LogWarning(
                "Closed stale run {RunId} (tenant {TenantId}) from {From} to {To}/{Code}: {Message}",
                run.ItemId, tenantId, run.Status, newStatus, errorCode, message);

            try
            {
                await _db.PublishAsync(
                    RedisChannel.Literal(FunctionQueueKeys.SyncChannel(run.ItemId)), FunctionWireMapping.ToWire(newStatus));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug("Could not publish the close of run {RunId}: {Message}", run.ItemId, ex.Message);
            }

            return 1;
        }
    }
}
