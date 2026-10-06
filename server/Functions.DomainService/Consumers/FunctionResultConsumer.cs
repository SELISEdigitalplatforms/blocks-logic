using System.Globalization;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Functions.DomainService.Consumers
{
    /// <summary>
    /// Consumes <c>functions:results</c>: the Worker's half of the delivery contract
    /// (plan/PROTOCOL.md, DECISIONS D1). This is the single Mongo writer for run outcomes —
    /// the runner never writes MongoDB and holds no tenant database credentials, by design.
    /// <para>
    /// <b>Fast in, fast out.</b> A result is persisted and acknowledged in a handful of Redis and
    /// Mongo round trips. Output actions are <i>not</i> run here: a slow endpoint can take minutes
    /// across its retries, and an entry held that long passes the reclaim idle threshold, is
    /// claimed by another Worker and applied — and its actions sent — a second time. A result
    /// with output actions is instead handed to <c>functions:outputs</c>
    /// (<see cref="FunctionOutputActionConsumer"/>), which has its own concurrency, per-tenant
    /// fairness and per-action idempotency markers.
    /// </para>
    /// <para>
    /// <b>Nothing on the entry is trusted beyond what the run record confirms.</b> The run must
    /// exist in the tenant the entry names, belong to the function it names, and be waiting for
    /// the attempt it names; the result and log keys must be that run's own. A result for an
    /// older attempt is dropped; one that fails validation is dead-lettered to
    /// <c>functions:dead-results</c> so it is visible rather than silently lost. The write itself is
    /// conditional (see <see cref="IFunctionRunRepository.ApplyResultAsync"/>), so a redelivery is
    /// a no-op for the record — and the follow-ups (output hand-off, retry scheduling, the sync
    /// wake-up) are each idempotent, so a redelivery after a crash half-way through them simply
    /// finishes the job.
    /// </para>
    /// </summary>
    public sealed class FunctionResultConsumer : BackgroundService
    {
        private const int MaxConcurrentResults = 10;
        private const int MaxDeliveryAttempts = 5;
        private static readonly TimeSpan ReclaimIdle = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ReclaimInterval = TimeSpan.FromSeconds(30);

        private readonly IDatabase _db;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionRunLogRepository _logRepository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly IFunctionImageRecoveryService _imageRecovery;
        private readonly IFunctionRepository _functionRepository;
        private readonly ILogger<FunctionResultConsumer> _logger;

        public FunctionResultConsumer(
            ICacheClient cache,
            IFunctionRunRepository runRepository,
            IFunctionRunLogRepository logRepository,
            IFunctionVersionRepository versionRepository,
            IFunctionImageRecoveryService imageRecovery,
            IFunctionRepository functionRepository,
            ILogger<FunctionResultConsumer> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            _db = cache.CacheDatabase();
            _runRepository = runRepository;
            _logRepository = logRepository;
            _versionRepository = versionRepository;
            _imageRecovery = imageRecovery;
            _functionRepository = functionRepository;
            _logger = logger;
        }

        /// <summary>What processing one entry decided; the loop acknowledges or dead-letters on it.</summary>
        internal enum ResultDisposition
        {
            /// <summary>This delivery wrote the run's outcome.</summary>
            Applied,

            /// <summary>Nothing to write — a redelivery or a superseded attempt. Acknowledged.</summary>
            Ignored,

            /// <summary>The entry is not a result this Worker can accept. Dead-lettered.</summary>
            Rejected,
        }

        internal sealed record ResultOutcome(ResultDisposition Disposition, string? Reason = null)
        {
            public static readonly ResultOutcome Applied = new(ResultDisposition.Applied);

            public static ResultOutcome Ignored(string reason) => new(ResultDisposition.Ignored, reason);

            public static ResultOutcome Rejected(string reason) => new(ResultDisposition.Rejected, reason);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumerName = $"{Environment.MachineName}-{Environment.ProcessId}";
            var consumer = new FunctionResultGroupConsumer(
                _db, _logger, FunctionQueueKeys.ResultsStream, consumerName);
            await consumer.EnsureGroupAsync();

            using var gate = new SemaphoreSlim(MaxConcurrentResults);
            // Wakes the idle wait below the moment a runner reports a result.
            using var wakeup = await StreamWakeup.SubscribeAsync(_db, FunctionQueueKeys.ResultsNudgeChannel);
            var nextReclaim = DateTimeOffset.UtcNow;

            _logger.LogInformation("Consuming {Stream} as {Consumer}", FunctionQueueKeys.ResultsStream, consumerName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var entries = await consumer.ReadNewAsync(count: MaxConcurrentResults);

                    if (entries.Count == 0 && DateTimeOffset.UtcNow >= nextReclaim)
                    {
                        nextReclaim = DateTimeOffset.UtcNow.Add(ReclaimInterval);
                        entries = await consumer.ReclaimAbandonedAsync(ReclaimIdle, count: MaxConcurrentResults);
                    }

                    if (entries.Count == 0)
                    {
                        await wakeup.WaitAsync(TimeSpan.FromMilliseconds(250), stoppingToken);
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
                    _logger.LogError(ex, "The Functions result loop hit an unexpected error");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }

        private async Task HandleAsync(
            FunctionResultGroupConsumer consumer, ResultStreamEntry entry, SemaphoreSlim gate, CancellationToken stoppingToken)
        {
            try
            {
                var outcome = await ProcessAsync(entry, stoppingToken);
                if (outcome.Disposition == ResultDisposition.Rejected)
                {
                    await consumer.DeadLetterAsync(entry, outcome.Reason ?? "rejected");
                }
                else
                {
                    if (outcome.Disposition == ResultDisposition.Ignored)
                    {
                        _logger.LogInformation("Result entry {Id} changed nothing: {Reason}", entry.Id, outcome.Reason);
                    }
                    await consumer.AcknowledgeAsync(entry.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process result entry {Id}", entry.Id);

                // A well-formed entry from our own runner is not expected to ever exhaust its
                // budget; this exists for the case where processing keeps failing for a reason
                // that will never resolve itself (a bug, a permanently malformed field) — left
                // un-acknowledged forever, that would block this stream for every tenant behind
                // it. Deliveries below the bound are simply retried by XAUTOCLAIM after
                // ReclaimIdle, with no special handling.
                try
                {
                    var deliveries = await consumer.DeliveryCountAsync(entry.Id);
                    if (deliveries >= MaxDeliveryAttempts)
                    {
                        await consumer.DeadLetterAsync(entry, $"delivered {deliveries} times without completing: {ex.Message}");
                    }
                }
                catch (Exception deadLetterEx)
                {
                    _logger.LogError(deadLetterEx, "Could not dead-letter result entry {Id}; it stays pending", entry.Id);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        internal async Task<ResultOutcome> ProcessAsync(ResultStreamEntry entry, CancellationToken cancellationToken)
        {
            var runId = entry.Get("runId");
            var tenantId = entry.Get("tenantId");

            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(tenantId))
            {
                // Without both, the record cannot be located (blocks-logic is
                // database-per-tenant) — this would only happen against a runner deployment
                // older than the tenantId/functionId contract addition in PROTOCOL.md.
                _logger.LogError(
                    "Rejecting a result entry with no runId/tenantId (runId={RunId}, tenantId={TenantId}) — " +
                    "is the runner on a version that predates the tenantId/functionId contract fields?",
                    runId, tenantId);
                return ResultOutcome.Rejected("the entry carries no runId or tenantId");
            }

            if (!int.TryParse(entry.Get("attempt"), NumberStyles.None, CultureInfo.InvariantCulture, out var attempt)
                || attempt < 1)
            {
                // No longer defaulted to 1: guessing the attempt is exactly how a late result
                // from attempt 1 could land on top of attempt 2's record.
                return ResultOutcome.Rejected($"the entry carries no valid attempt ('{entry.Get("attempt")}')");
            }

            var status = FunctionWireMapping.ToRunStatus(entry.Get("status"), out var recognisedStatus);
            if (!recognisedStatus)
            {
                _logger.LogError("Unrecognised run status '{Status}' for run {RunId}; recording as FAILED", entry.Get("status"), runId);
            }
            else if (!FunctionWireMapping.IsTerminal(status))
            {
                // The runner publishes a result exactly once, when the run is over. A "result"
                // saying QUEUED or RUNNING would stamp CompletedAt on a live run.
                return ResultOutcome.Rejected($"'{entry.Get("status")}' is not a terminal status");
            }
            var errorCode = FunctionWireMapping.ToErrorCode(entry.Get("errorCode"));

            // The key names come off the entry, so they are checked rather than followed: the only
            // keys a result may be read from are this run's own.
            var resultKey = entry.Get("resultKey");
            if (!string.IsNullOrEmpty(resultKey) && resultKey != FunctionQueueKeys.Result(runId))
            {
                return ResultOutcome.Rejected($"resultKey '{resultKey}' does not belong to run {runId}");
            }
            var logsKey = entry.Get("logsKey");
            if (!string.IsNullOrEmpty(logsKey) && logsKey != FunctionQueueKeys.Logs(runId))
            {
                return ResultOutcome.Rejected($"logsKey '{logsKey}' does not belong to run {runId}");
            }

            // Where this result's time goes, logged once it is published (StepTimer). "queued" is
            // how long the entry sat on the stream before this Worker picked it up.
            var timer = new StepTimer();
            var queuedMs = StreamEntryAgeMs(entry.Id);

            // --- the run record is the authority on everything the entry claims -------------
            var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
            timer.Mark("read");
            if (run is null)
            {
                return ResultOutcome.Rejected($"no run {runId} exists in tenant {tenantId}");
            }

            if (!string.IsNullOrEmpty(run.TenantId) && !string.Equals(run.TenantId, tenantId, StringComparison.Ordinal))
            {
                return ResultOutcome.Rejected($"run {runId} is recorded for another tenant");
            }

            var functionId = entry.Get("functionId");
            if (!string.IsNullOrEmpty(functionId) && !string.Equals(functionId, run.FunctionId, StringComparison.Ordinal))
            {
                return ResultOutcome.Rejected($"the entry names function {functionId} but run {runId} belongs to {run.FunctionId}");
            }

            if (attempt < run.Attempt)
            {
                return ResultOutcome.Ignored($"attempt {attempt} of run {runId} was superseded by attempt {run.Attempt}");
            }
            if (attempt > run.Attempt)
            {
                return ResultOutcome.Rejected($"run {runId} has not started attempt {attempt} (current attempt is {run.Attempt})");
            }

            var applied = false;
            if (AcceptsResult(run))
            {
                await CopyLogsAsync(tenantId, run.FunctionId, runId, attempt, logsKey, cancellationToken);
                timer.Mark("logs");

                string? result = null;
                if (!string.IsNullOrEmpty(resultKey))
                {
                    RedisValue value = await _db.StringGetAsync(resultKey);
                    result = value.IsNullOrEmpty ? null : (string?)value;
                }

                var startedAt = ParseDate(entry.Get("startedAt"));
                var completedAt = ParseDate(entry.Get("completedAt")) ?? DateTime.UtcNow;

                var written = await _runRepository.ApplyResultAsync(
                    tenantId, runId, attempt, status, errorCode, entry.Get("errorMessage"),
                    result, ParseNullableInt(entry.Get("exitCode")), ParseNullableLong(entry.Get("durationMs")),
                    ParseNullableLong(entry.Get("peakMemoryBytes")), ParseNullableLong(entry.Get("cpuUsageMs")),
                    entry.Get("runnerId"), startedAt, completedAt,
                    entry.GetBool("truncated"), SandboxReport(entry), cancellationToken);
                timer.Mark("result+apply");

                switch (written)
                {
                    case ApplyResultOutcome.Applied:
                        applied = true;
                        break;
                    case ApplyResultOutcome.StaleAttempt:
                        // A retry reset the run between the read above and the write.
                        return ResultOutcome.Ignored($"attempt {attempt} of run {runId} was superseded while it was being applied");
                    case ApplyResultOutcome.NotFound:
                        return ResultOutcome.Ignored($"run {runId} was deleted while its result was being applied");
                    case ApplyResultOutcome.UnknownAttempt:
                        return ResultOutcome.Rejected($"run {runId} has not started attempt {attempt}");
                    case ApplyResultOutcome.Duplicate:
                    default:
                        // Another delivery of the same result got there first. Fall through to
                        // the follow-ups, which are idempotent.
                        break;
                }

                run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
                timer.Mark("reread");
                if (run is null || run.Attempt != attempt)
                {
                    return ResultOutcome.Ignored($"run {runId} moved on while its result was being applied");
                }

                if (applied)
                {
                    await _imageRecovery.HandleRunOutcomeAsync(tenantId, run, errorCode, cancellationToken);
                }
            }

            // Everything below is safe to repeat, and is repeated on a redelivery: that is what
            // finishes the job when a Worker died between writing the result and acknowledging it.
            await FollowUpAsync(tenantId, run, cancellationToken);
            timer.Mark("followup");

            await _db.PublishAsync(
                RedisChannel.Literal(FunctionQueueKeys.SyncChannel(runId)), FunctionWireMapping.ToWire(run.Status));
            timer.Mark("publish");
            _logger.LogInformation(
                "Result of run {RunId} published {TotalMs} ms after pick-up (queued {QueuedMs} ms): {Steps}",
                runId, timer.ElapsedMs, queuedMs, timer.ToString());

            // After the publish, so the caller already has the answer: where this call's time went,
            // for the run's Timing group. Best effort — a run is never failed over its timings.
            if (applied)
            {
                try
                {
                    var timings = StepTimer.ParseCompact(entry.Get(FunctionQueueKeys.ResultTimingsField));
                    timings.Add(new Models.RunTiming { Group = "result", Step = "queued", Ms = Math.Max(0, queuedMs) });
                    timings.AddRange(timer.ToTimings("result"));
                    await _runRepository.SetTimingsAsync(tenantId, runId, attempt, timings, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Could not record the timings of run {RunId} ({ExceptionType})", runId, ex.GetType().Name);
                }
            }

            return applied
                ? ResultOutcome.Applied
                : ResultOutcome.Ignored($"run {runId} already holds the outcome of attempt {attempt}");
        }

        /// <summary>
        /// Whether the record is still open to this attempt's result — mirrors the repository's
        /// own filter, so the logs are only copied for a result that is going to be written.
        /// </summary>
        private static bool AcceptsResult(FunctionRunEntity run) =>
            !FunctionWireMapping.IsTerminal(run.Status)
                ? run.Status != RunStatus.OutputProcessing
                : IsPlatformDetermined(run.ErrorCode);

        private static bool IsPlatformDetermined(RunErrorCode code) => FunctionWireMapping.IsPlatformDetermined(code);

        /// <summary>Milliseconds since a stream entry was added, from its id (<c>ms-seq</c>); -1 if unreadable.</summary>
        private static long StreamEntryAgeMs(RedisValue id)
        {
            var text = id.ToString();
            var dash = text.IndexOf('-', StringComparison.Ordinal);
            return long.TryParse(dash > 0 ? text[..dash] : text, NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ms
                : -1;
        }

        private async Task FollowUpAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken)
        {
            if (run.Status == RunStatus.Succeeded && run.OutputResults.Count == 0)
            {
                var (actions, _) = await FunctionOutputConfig.ResolveAsync(
                    _versionRepository, _functionRepository, tenantId, run, cancellationToken);
                if (!FunctionOutputConfig.HasEnabledActions(actions)) return;

                if (await _runRepository.TryBeginOutputProcessingAsync(tenantId, run.ItemId, run.Attempt, cancellationToken))
                {
                    run.Status = RunStatus.OutputProcessing;
                    await EnqueueOutputJobAsync(tenantId, run);
                }
                return;
            }

            if (run.Status == RunStatus.OutputProcessing)
            {
                // Handed off already — but possibly by a Worker that died before the job reached
                // the stream. Adding it again is harmless: the output consumer drops a job whose
                // run is no longer OUTPUT_PROCESSING, and never sends an action twice.
                await EnqueueOutputJobAsync(tenantId, run);
                return;
            }

            if (FunctionWireMapping.IsRetryCandidate(run))
            {
                await ScheduleRetryIfEligibleAsync(tenantId, run, cancellationToken);
            }
        }

        private Task EnqueueOutputJobAsync(string tenantId, FunctionRunEntity run) =>
            _db.StreamAddAsync(FunctionWorkerQueueKeys.OutputsStream,
            [
                new NameValueEntry("runId", run.ItemId),
                new NameValueEntry("tenantId", tenantId),
                new NameValueEntry("functionId", run.FunctionId),
                new NameValueEntry("attempt", run.Attempt),
                new NameValueEntry("enqueuedAt", DateTimeOffset.UtcNow.ToString("O")),
            ]);

        private async Task CopyLogsAsync(
            string tenantId, string functionId, string runId, int attempt, string? logsKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(logsKey)) return;

            // A redelivery must not insert every line a second time. The marker is written after
            // the insert, so a crash in between can still duplicate — but only then.
            var copiedKey = FunctionWorkerQueueKeys.LogsCopied(runId, attempt);
            if (await _db.KeyExistsAsync(copiedKey)) return;

            var lines = await _db.ListRangeAsync(logsKey);
            if (lines.Length == 0) return;

            var logs = new List<FunctionRunLogEntity>(lines.Length);
            var seq = 0;
            foreach (var line in lines)
            {
                seq++;
                var (level, message, timestamp, data) = ParseLogLine(line!);
                logs.Add(new FunctionRunLogEntity
                {
                    ItemId = Guid.NewGuid().ToString(),
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow,
                    RunId = runId,
                    FunctionId = functionId,
                    Seq = seq,
                    Timestamp = timestamp,
                    Level = level,
                    Message = message,
                    Data = data,
                });
            }

            await _logRepository.InsertManyAsync(tenantId, logs, cancellationToken);
            await _db.StringSetAsync(copiedKey, "1", FunctionWorkerQueueKeys.LogsCopiedTtl);
        }

        private static (string Level, string Message, DateTime Timestamp, string? Data) ParseLogLine(string line)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;
                var level = root.TryGetProperty("level", out var l) ? l.GetString() ?? "info" : "info";
                var message = root.TryGetProperty("msg", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                var timestamp = root.TryGetProperty("ts", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String
                    && DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
                    ? parsed
                    : DateTime.UtcNow;
                var data = root.TryGetProperty("data", out var d) ? d.GetRawText() : null;
                return (level, message, timestamp, data);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                // The bootstrap's own output is well-formed by construction; a raw line here
                // means some dependency wrote straight to stdout. Keep it, attributed clearly,
                // rather than silently dropping output a developer might need to see.
                return ("info", line, DateTime.UtcNow, null);
            }
        }

        private async Task ScheduleRetryIfEligibleAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken)
        {
            // Retries only apply to a deployed version. A Test run has nothing durable to
            // rebuild against reliably (see FunctionInvocationService.ReplayAsync), and its
            // MaxAttempts is set from the function's own policy regardless, so this also
            // naturally covers the "policy says 1 attempt" case via the count check below.
            // Same budget test the synchronous HTTP wait relies on (FunctionWireMapping.WillBeRetried).
            if (!FunctionWireMapping.WillBeRetried(run)) return;

            var version = await _versionRepository.GetByIdAsync(tenantId, run.VersionId, cancellationToken);
            if (version is null) return;

            var nextAttempt = run.Attempt + 1;
            var delay = version.Retry.DelayFor(nextAttempt);
            var dueAt = DateTimeOffset.UtcNow.Add(delay).ToUnixTimeSeconds();

            var member = System.Text.Json.JsonSerializer.Serialize(new
            {
                runId = run.ItemId,
                tenantId,
                functionId = run.FunctionId,
                versionId = run.VersionId,
                attempt = nextAttempt,
            });

            // NX: a redelivered result must not push an already-scheduled retry further out.
            var added = await _db.SortedSetAddAsync(FunctionQueueKeys.RetryQueue, member, dueAt, When.NotExists);
            if (added)
            {
                _logger.LogInformation(
                    "Scheduled retry {Attempt}/{Max} for run {RunId} in {Delay}", nextAttempt, run.MaxAttempts, run.ItemId, delay);
            }
        }

        /// <summary>
        /// The optional warm-sandbox fields (sandbox/REUSE.md). A runner that predates reuse sends
        /// none of them, and each one missing stays null rather than defaulting to "fresh" or
        /// "kept" — the run record should say "not reported", which is the truth.
        /// </summary>
        internal static RunSandboxReport SandboxReport(ResultStreamEntry entry)
        {
            var reused = entry.Get("reused") switch
            {
                "1" or "true" => true,
                "0" or "false" => (bool?)false,
                _ => null,
            };
            var discard = entry.Get("discard");
            return new RunSandboxReport(
                reused,
                string.IsNullOrWhiteSpace(discard) ? null : discard,
                ParseNullableLong(entry.Get("handoverMs")));
        }

        private static DateTime? ParseDate(string? value) =>
            !string.IsNullOrEmpty(value) &&
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : null;

        private static int? ParseNullableInt(string? value) =>
            int.TryParse(value, out var parsed) ? parsed : null;

        private static long? ParseNullableLong(string? value) =>
            long.TryParse(value, out var parsed) ? parsed : null;
    }
}
