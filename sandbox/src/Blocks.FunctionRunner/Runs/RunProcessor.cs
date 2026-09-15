using System.Globalization;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Blocks.FunctionRunner.Runs
{
    /// <summary>
    /// Executes one claimed run, end to end.
    /// <para>
    /// The ordering in <see cref="ProcessAsync"/> is the delivery contract and is not
    /// rearrangeable: take the lease, write the envelope, run the sandbox, <b>persist the result
    /// durably</b>, publish it on the results stream, and only then acknowledge the run entry.
    /// Acknowledging any earlier turns a crash into lost work; acknowledging later would replay
    /// a run that already reported. The logic Worker is the single Mongo writer, so this side
    /// only has to get the handover right.
    /// </para>
    /// </summary>
    public sealed class RunProcessor
    {
        private readonly IDatabase _db;
        private readonly ISandbox _sandbox;
        private readonly IImageResolver _images;
        private readonly HostBudget _budget;
        private readonly RunnerOptions _options;
        private readonly ILogger<RunProcessor> _logger;

        public RunProcessor(
            IDatabase db,
            ISandbox sandbox,
            IImageResolver images,
            HostBudget budget,
            IOptions<RunnerOptions> options,
            ILogger<RunProcessor> logger)
        {
            _db = db;
            _sandbox = sandbox;
            _images = images;
            _budget = budget;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>Outcome of trying to process one stream entry.</summary>
        public enum Disposition
        {
            /// <summary>Finished; the entry may be acknowledged.</summary>
            Complete,

            /// <summary>The host or the function is at capacity; leave it pending and retry.</summary>
            Deferred,

            /// <summary>Another runner owns it; leave it alone.</summary>
            NotOurs,
        }

        public async Task<Disposition> ProcessAsync(RunJob job, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(job);

            var runKey = RedisKeys.Run(job.RunId);
            var hash = await _db.HashGetAllAsync(runKey).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                // The run record expired or was never written. There is nothing to execute and
                // nothing to report to; acknowledging is the only sane outcome.
                _logger.LogWarning("Run {RunId} has no record at {Key}; discarding the entry", job.RunId, runKey);
                return Disposition.Complete;
            }

            var fields = hash.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString(), StringComparer.Ordinal);
            var limits = ReadLimits(fields);

            // Admission. Neither refusal is a failure: the entry stays pending and is retried.
            using var reservation = _budget.TryReserve(limits.MemoryBytes);
            if (reservation is null)
            {
                _logger.LogDebug("Host is at capacity ({Active}/{Capacity}); deferring run {RunId}",
                    _budget.Active, _budget.Capacity, job.RunId);
                return Disposition.Deferred;
            }

            await using var slot = await FunctionConcurrency.TryEnterAsync(
                _db, job.FunctionId, job.RunId, limits.FunctionConcurrency).ConfigureAwait(false);
            if (slot is null)
            {
                _logger.LogDebug("Function {FunctionId} is at its concurrency limit; deferring run {RunId}",
                    job.FunctionId, job.RunId);
                return Disposition.Deferred;
            }

            await using var lease = await RunLease.TryAcquireAsync(
                _db, _logger, job.RunId, TimeSpan.FromMilliseconds(_options.LeaseMs), token).ConfigureAwait(false);
            if (lease is null)
            {
                _logger.LogInformation("Run {RunId} is already leased by another runner", job.RunId);
                return Disposition.NotOurs;
            }

            var startedAt = DateTimeOffset.UtcNow;
            await SetStatusAsync(runKey, RunStatuses.Starting).ConfigureAwait(false);

            var runDir = Path.Combine(_options.RunsDir, job.RunId);
            try
            {
                // --- image ------------------------------------------------------------
                var image = await _images.EnsureAsync(job.Image, token).ConfigureAwait(false);
                if (image is null)
                {
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.ImagePullFailed,
                        $"the image '{job.Image}' could not be resolved", null, 0, null, null, null, null, false)
                        .ConfigureAwait(false);
                    return Disposition.Complete;
                }

                // --- envelope ---------------------------------------------------------
                string envelopePath;
                try
                {
                    var envelope = fields.TryGetValue("envelope", out var e) ? e : "{}";
                    envelopePath = ExecutionEnvelope.Write(runDir, envelope);
                }
                catch (ExecutionEnvelope.ForbiddenContentException ex)
                {
                    _logger.LogError("Refusing to run {RunId}: {Message}", job.RunId, ex.Message);
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.RuntimeStartFailed,
                        ex.Message, null, 0, null, null, null, null, false).ConfigureAwait(false);
                    return Disposition.Complete;
                }

                // --- execute ----------------------------------------------------------
                await SetStatusAsync(runKey, RunStatuses.Running).ConfigureAwait(false);
                var result = await _sandbox.RunAsync(job.RunId, image, envelopePath, limits, lease.Token)
                    .ConfigureAwait(false);

                if (lease.LeaseLost)
                {
                    // Someone else owns this run now. Say nothing: reporting would race with
                    // the runner that legitimately holds it.
                    _logger.LogWarning("Abandoning run {RunId} after losing its lease", job.RunId);
                    return Disposition.NotOurs;
                }

                if (result.HostFailure is not null)
                {
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.SandboxStartFailed,
                        result.HostFailure, result.ExitCode, result.DurationMs, null, null, null, null, false)
                        .ConfigureAwait(false);
                    return Disposition.Complete;
                }

                var (status, errorCode, errorMessage) = RunOutcome.Map(
                    result.OomKilled, result.ExitCode, result.TimedOut, lease.CancelRequested, result.Output);

                // What this run actually cost, fed back into admission. Reserving a run's whole
                // limit is safe and wasteful; the budget narrows that to the observed p95 only
                // because every completed run reports its peak here.
                _budget.Observe(result.PeakMemoryBytes);

                await CompleteAsync(
                    job, runKey, startedAt, status, errorCode, errorMessage, result.ExitCode, result.DurationMs,
                    result.PeakMemoryBytes, result.CpuUsageMs, result.Output.ResultJson, result.Output.Logs,
                    result.Output.Truncated)
                    .ConfigureAwait(false);

                return Disposition.Complete;
            }
            finally
            {
                CleanUp(runDir);
            }
        }

        /// <summary>
        /// Persists the outcome and publishes it. The result and logs are written to Redis
        /// <b>before</b> the stream entry, so a crash between the two replays the run rather
        /// than losing it.
        /// </summary>
        private async Task CompleteAsync(
            RunJob job, string runKey, DateTimeOffset startedAt,
            string status, string? errorCode, string? errorMessage,
            int? exitCode, long durationMs, long? peakMemory, long? cpuUsageMs,
            string? resultJson, List<string>? logs, bool truncated)
        {
            var completedAt = DateTimeOffset.UtcNow;
            string? resultKey = null;
            string? logsKey = null;

            if (resultJson is not null)
            {
                resultKey = RedisKeys.Result(job.RunId);
                await _db.StringSetAsync(resultKey, resultJson, RedisKeys.ResultTtl).ConfigureAwait(false);
            }

            if (logs is { Count: > 0 })
            {
                logsKey = RedisKeys.Logs(job.RunId);
                var values = new RedisValue[logs.Count];
                for (var i = 0; i < logs.Count; i++) values[i] = logs[i];
                await _db.KeyDeleteAsync(logsKey).ConfigureAwait(false);
                await _db.ListRightPushAsync(logsKey, values).ConfigureAwait(false);
                await _db.KeyExpireAsync(logsKey, RedisKeys.LogsTtl).ConfigureAwait(false);
            }

            await SetStatusAsync(runKey, status).ConfigureAwait(false);

            var entry = new NameValueEntry[]
            {
                new("runId", job.RunId),
                // functionId and tenantId are echoed back from the job because blocks-logic is
                // database-per-tenant: a consumer holding only a runId cannot find the record to
                // update, and depending on function:run:{runId} still being inside its TTL would
                // make late consumption silently lossy.
                new("functionId", job.FunctionId),
                new("tenantId", job.TenantId ?? string.Empty),
                new("status", status),
                new("errorCode", errorCode ?? string.Empty),
                new("errorMessage", Truncate(errorMessage, 4096) ?? string.Empty),
                new("exitCode", exitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                new("durationMs", durationMs.ToString(CultureInfo.InvariantCulture)),
                new("peakMemoryBytes", peakMemory?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                new("cpuUsageMs", cpuUsageMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                new("runnerId", _options.RunnerId),
                new("startedAt", startedAt.ToString("O")),
                new("completedAt", completedAt.ToString("O")),
                new("resultKey", resultKey ?? string.Empty),
                new("logsKey", logsKey ?? string.Empty),
                new("truncated", truncated ? "true" : "false"),
                new("attempt", job.Attempt.ToString(CultureInfo.InvariantCulture)),
                new("protocol", RedisKeys.ProtocolVersion.ToString(CultureInfo.InvariantCulture)),
            };

            await _db.StreamAddAsync(RedisKeys.ResultsStream, entry).ConfigureAwait(false);

            // Wake anyone waiting synchronously on this run.
            await _db.PublishAsync(
                RedisChannel.Literal(RedisKeys.SyncChannel(job.RunId)), status).ConfigureAwait(false);

            _logger.LogInformation(
                "Run {RunId} finished as {Status} in {DurationMs}ms (exit {ExitCode})",
                job.RunId, status, durationMs, exitCode);
        }

        private Task SetStatusAsync(string runKey, string status)
            => _db.HashSetAsync(runKey,
            [
                new HashEntry("status", status),
                new HashEntry("statusAt", DateTimeOffset.UtcNow.ToString("O")),
            ]);

        private static RunLimits ReadLimits(IReadOnlyDictionary<string, string> fields)
        {
            // Whatever the control plane asked for, Clamp is the last word.
            return RunLimits.Clamp(
                ReadInt(fields, "cpuMillicores"),
                ReadLong(fields, "memoryBytes"),
                ReadInt(fields, "pidLimit"),
                ReadLong(fields, "tmpfsBytes"),
                ReadInt(fields, "timeoutSeconds"),
                ReadInt(fields, "concurrency"));
        }

        private static int? ReadInt(IReadOnlyDictionary<string, string> f, string key)
            => f.TryGetValue(key, out var v) && int.TryParse(v, CultureInfo.InvariantCulture, out var i) ? i : null;

        private static long? ReadLong(IReadOnlyDictionary<string, string> f, string key)
            => f.TryGetValue(key, out var v) && long.TryParse(v, CultureInfo.InvariantCulture, out var l) ? l : null;

        private static string? Truncate(string? value, int max)
            => value is null || value.Length <= max ? value : value[..max];

        private void CleanUp(string runDir)
        {
            try
            {
                if (Directory.Exists(runDir)) Directory.Delete(runDir, recursive: true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning("Could not remove run directory {Dir}: {Message}", runDir, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Could not remove run directory {Dir}: {Message}", runDir, ex.Message);
            }
        }
    }
}
