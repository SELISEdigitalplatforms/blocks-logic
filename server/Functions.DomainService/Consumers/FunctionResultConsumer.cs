using System.Globalization;
using Blocks.Genesis;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
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
    /// Processes entries with <b>bounded concurrency</b> rather than one at a time. A result
    /// whose function has output actions can legitimately block for a while retrying an
    /// external endpoint (<see cref="Services.OutputActionProcessor"/> reuses the function's
    /// own retry policy, up to a 5-minute default backoff cap) — a single-threaded consumer
    /// loop would let one slow endpoint stall every other tenant's results behind it. A
    /// semaphore instead lets the stream keep draining while a handful of slow ones work
    /// through their retries in the background.
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
        private readonly IFunctionRepository _functionRepository;
        private readonly Services.IOutputActionProcessor _outputActionProcessor;
        private readonly ILogger<FunctionResultConsumer> _logger;

        public FunctionResultConsumer(
            ICacheClient cache,
            IFunctionRunRepository runRepository,
            IFunctionRunLogRepository logRepository,
            IFunctionVersionRepository versionRepository,
            IFunctionRepository functionRepository,
            Services.IOutputActionProcessor outputActionProcessor,
            ILogger<FunctionResultConsumer> logger)
        {
            _db = cache.CacheDatabase();
            _runRepository = runRepository;
            _logRepository = logRepository;
            _versionRepository = versionRepository;
            _functionRepository = functionRepository;
            _outputActionProcessor = outputActionProcessor;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumerName = $"{Environment.MachineName}-{Environment.ProcessId}";
            var consumer = new FunctionResultGroupConsumer(
                _db, _logger, FunctionQueueKeys.ResultsStream, consumerName);
            await consumer.EnsureGroupAsync();

            using var gate = new SemaphoreSlim(MaxConcurrentResults);
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
                await ProcessAsync(entry, stoppingToken);
                await consumer.AcknowledgeAsync(entry.Id);
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
            var runId = entry.Get("runId");
            var tenantId = entry.Get("tenantId");
            var functionId = entry.Get("functionId");

            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(tenantId))
            {
                // Without both, the record cannot be located (blocks-logic is
                // database-per-tenant) — this would only happen against a runner deployment
                // older than the tenantId/functionId contract addition in PROTOCOL.md.
                _logger.LogError(
                    "Dropping a result entry with no runId/tenantId (runId={RunId}, tenantId={TenantId}) — " +
                    "is the runner on a version that predates the tenantId/functionId contract fields?",
                    runId, tenantId);
                return;
            }

            var status = FunctionWireMapping.ToRunStatus(entry.Get("status"), out var recognisedStatus);
            if (!recognisedStatus)
            {
                _logger.LogError("Unrecognised run status '{Status}' for run {RunId}; recording as FAILED", entry.Get("status"), runId);
            }
            var errorCode = FunctionWireMapping.ToErrorCode(entry.Get("errorCode"));
            var attempt = entry.GetInt("attempt", 1);

            var resultKey = entry.Get("resultKey");
            string? result = null;
            if (!string.IsNullOrEmpty(resultKey))
            {
                RedisValue value = await _db.StringGetAsync(resultKey);
                result = value.IsNullOrEmpty ? null : (string?)value;
            }

            var logsKey = entry.Get("logsKey");
            await CopyLogsAsync(tenantId, functionId ?? string.Empty, runId, logsKey, cancellationToken);

            var startedAt = ParseDate(entry.Get("startedAt"));
            var completedAt = ParseDate(entry.Get("completedAt")) ?? DateTime.UtcNow;

            await _runRepository.ApplyResultAsync(
                tenantId, runId, attempt, status, errorCode, entry.Get("errorMessage"),
                result, ParseNullableInt(entry.Get("exitCode")), ParseNullableLong(entry.Get("durationMs")),
                ParseNullableLong(entry.Get("peakMemoryBytes")), entry.Get("runnerId"), startedAt, completedAt,
                entry.GetBool("truncated"), cancellationToken);

            var run = await _runRepository.GetByIdAsync(tenantId, runId, cancellationToken);
            if (run is null) return;

            if (status == RunStatus.Succeeded)
            {
                await ProcessOutputActionsAsync(tenantId, run, cancellationToken);
            }
            else if (FunctionWireMapping.IsRetryable(status, errorCode))
            {
                await ScheduleRetryIfEligibleAsync(tenantId, run, cancellationToken);
            }

            await _db.PublishAsync(
                RedisChannel.Literal(FunctionQueueKeys.SyncChannel(runId)), FunctionWireMapping.ToWire(run.Status));
        }

        private async Task CopyLogsAsync(
            string tenantId, string functionId, string runId, string? logsKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(logsKey)) return;

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
            catch (System.Text.Json.JsonException)
            {
                // The bootstrap's own output is well-formed by construction; a raw line here
                // means some dependency wrote straight to stdout. Keep it, attributed clearly,
                // rather than silently dropping output a developer might need to see.
                return ("info", line, DateTime.UtcNow, null);
            }
        }

        private async Task ProcessOutputActionsAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken)
        {
            var (actions, retryPolicy) = await ResolveOutputConfigAsync(tenantId, run, cancellationToken);
            if (actions.Count == 0 || !actions.Any(a => a.Enabled)) return;

            await _runRepository.ApplyStatusOnlyAsync(tenantId, run.ItemId, RunStatus.OutputProcessing, cancellationToken);

            var chain = await _outputActionProcessor.ProcessAsync(tenantId, run, actions, retryPolicy, cancellationToken);

            await _runRepository.ApplyOutputResultAsync(
                tenantId, run.ItemId, chain.Results,
                chain.AllSucceeded ? RunStatus.Succeeded : RunStatus.OutputFailed,
                chain.AllSucceeded ? null : "one or more output actions failed",
                cancellationToken);

            run.Status = chain.AllSucceeded ? RunStatus.Succeeded : RunStatus.OutputFailed;
        }

        private async Task<(List<Models.OutputAction> Actions, Models.RetryPolicy RetryPolicy)> ResolveOutputConfigAsync(
            string tenantId, FunctionRunEntity run, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(run.VersionId))
            {
                var version = await _versionRepository.GetByIdAsync(tenantId, run.VersionId, cancellationToken);
                if (version is not null) return (version.OutputActions, version.Retry);
            }

            var function = await _functionRepository.GetByIdAsync(tenantId, run.FunctionId, cancellationToken);
            return (function?.OutputActions ?? [], function?.Retry ?? new Models.RetryPolicy());
        }

        private async Task ScheduleRetryIfEligibleAsync(string tenantId, FunctionRunEntity run, CancellationToken cancellationToken)
        {
            // Retries only apply to a deployed version. A Test run has nothing durable to
            // rebuild against reliably (see FunctionInvocationService.ReplayAsync), and its
            // MaxAttempts is set from the function's own policy regardless, so this also
            // naturally covers the "policy says 1 attempt" case via the count check below.
            if (string.IsNullOrEmpty(run.VersionId)) return;
            if (run.Attempt >= run.MaxAttempts) return;

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

            await _db.SortedSetAddAsync(FunctionQueueKeys.RetryQueue, member, dueAt);
            _logger.LogInformation(
                "Scheduled retry {Attempt}/{Max} for run {RunId} in {Delay}", nextAttempt, run.MaxAttempts, delay, run.ItemId);
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
