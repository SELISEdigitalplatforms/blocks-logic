using System.Globalization;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Delegation;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Protocol;
using Blocks.FunctionRunner.Redis;
using Blocks.FunctionRunner.Sandbox;
using Blocks.FunctionRunner.SecretStore;
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
        private readonly Maintenance.IImageUsageLog? _usage;
        private readonly IRunSecretResolver _secrets;
        private readonly IRunAccessTokenResolver _accessTokens;
        private readonly RunnerOptions _options;
        private readonly ILogger<RunProcessor> _logger;

        public RunProcessor(
            IDatabase db,
            ISandbox sandbox,
            IImageResolver images,
            HostBudget budget,
            IRunSecretResolver secrets,
            IRunAccessTokenResolver accessTokens,
            IOptions<RunnerOptions> options,
            ILogger<RunProcessor> logger,
            Maintenance.IImageUsageLog? usage = null)
        {
            _db = db;
            _sandbox = sandbox;
            _images = images;
            _budget = budget;
            _usage = usage;
            _secrets = secrets;
            _accessTokens = accessTokens;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// Replaces the envelope's group handoff (a chown to the sandbox gid) so tests can run
        /// without root and can make it fail. Null — the only value in production — uses the
        /// real one.
        /// </summary>
        internal Action<string, int>? EnvelopeGroupHandoff { get; set; }

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

        /// <summary>
        /// Reports a run that never reached a sandbox — a test whose build failed, or that found
        /// no capacity — through the same result path as an executed one. Does nothing when the
        /// run's payload is gone (expired, or its function was deleted): there is no record left
        /// to report to, and writing the status would recreate the key.
        /// </summary>
        public async Task ReportUnexecutedAsync(
            RunJob job, string status, string errorCode, string errorMessage, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(job);
            var runKey = RedisKeys.Run(job.RunId);
            if (!await _db.KeyExistsAsync(runKey).ConfigureAwait(false)) return;

            await CompleteAsync(job, runKey, DateTimeOffset.UtcNow, status, errorCode, errorMessage,
                null, 0, null, null, null, null, false).ConfigureAwait(false);
        }

        /// <summary>True while the run's payload exists, i.e. it has not expired or been withdrawn.</summary>
        public async Task<bool> IsLiveAsync(string runId)
            => await _db.KeyExistsAsync(RedisKeys.Run(runId)).ConfigureAwait(false);

        /// <summary>True when the control plane has asked for the run to stop.</summary>
        public async Task<bool> IsCancelRequestedAsync(string runId)
            => await _db.KeyExistsAsync(RedisKeys.Cancel(runId)).ConfigureAwait(false);

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

            // A tenant's share of the fleet, before anything function-specific. The host budget
            // above is first-come-first-served, so without this one tenant's burst could hold
            // every slot on a runner and every other tenant waited behind it.
            await using var tenantSlot = string.IsNullOrEmpty(job.TenantId)
                ? null
                : await FunctionConcurrency.TryEnterTenantAsync(
                    _db, job.TenantId, job.RunId, _options.TenantSlotLimit(_budget.Capacity)).ConfigureAwait(false);
            if (tenantSlot is null && !string.IsNullOrEmpty(job.TenantId))
            {
                _logger.LogDebug(
                    "Tenant {TenantId} is at its share of {Limit} sandbox slot(s); deferring run {RunId}",
                    job.TenantId, _options.TenantSlotLimit(_budget.Capacity), job.RunId);
                return Disposition.Deferred;
            }

            // A test draws on its own budget, never the function's: clicking Test must not be
            // able to delay the traffic that function's deployed version is serving.
            await using var slot = job.IsTest
                ? await FunctionConcurrency.TryEnterTestAsync(_db, job.FunctionId, job.RunId).ConfigureAwait(false)
                : await FunctionConcurrency.TryEnterAsync(
                    _db, job.FunctionId, job.RunId, limits.FunctionConcurrency).ConfigureAwait(false);
            if (slot is null)
            {
                _logger.LogDebug("Function {FunctionId} is at its {Kind} concurrency limit; deferring run {RunId}",
                    job.FunctionId, job.IsTest ? "test" : "function", job.RunId);
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
                // The artifact, when the control plane sent one, is how this host produces the
                // image itself rather than asking a registry for what another host built.
                var image = await _images
                    .EnsureAsync(job.Image, token, job.ArtifactUrl, job.ArtifactSha256)
                    .ConfigureAwait(false);

                // What the cache evicts by. Stamped on resolve rather than on completion so a run
                // that fails still counts as use — the image was wanted, which is the question the
                // cache is asking.
                if (image is not null) _usage?.Touch(image);
                if (image is null)
                {
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.ImagePullFailed,
                        $"the image '{job.Image}' could not be resolved", null, 0, null, null, null, null, false)
                        .ConfigureAwait(false);
                    return Disposition.Complete;
                }

                // --- envelope ---------------------------------------------------------
                // What comes off the queue carries secret-bound variables as references; the
                // plaintext is fetched here, as late as possible, and exists only in this
                // method's memory and in the 0440 envelope file removed by CleanUp below. It is
                // never written back to the run record: a retry re-reads the references and
                // resolves them afresh, which is also what makes a rotated secret take effect on
                // the very next attempt.
                string envelopePath;
                IReadOnlyList<string> resolvedValues = [];
                try
                {
                    var envelope = fields.TryGetValue("envelope", out var e) ? e : "{}";

                    // Screened before anything is resolved, so an envelope the platform would
                    // refuse anyway never causes a secret to be read.
                    ExecutionEnvelope.Screen(envelope);

                    if (job.Protocol >= RedisKeys.RunProtocolVersion)
                    {
                        var prepared = await ResolveSecretsAsync(job, envelope, token).ConfigureAwait(false);
                        if (prepared.Failure is { } failure)
                        {
                            await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, failure.Code,
                                failure.Message, null, 0, null, null, null, null, false).ConfigureAwait(false);
                            return Disposition.Complete;
                        }
                        envelope = prepared.Envelope!;
                        resolvedValues = prepared.Values;
                    }

                    // After the screen, deliberately: it refuses an `accessToken` key, and this
                    // one is added by the runner from a grant, never carried by the queue.
                    var accessToken = await RedeemAccessTokenAsync(job, fields, envelope, token).ConfigureAwait(false);
                    if (accessToken is not null)
                    {
                        envelope = RunDelegation.Apply(envelope, accessToken);
                        resolvedValues = [.. resolvedValues, accessToken];
                    }

                    var delegated = accessToken is not null;
                    envelopePath = EnvelopeGroupHandoff is null
                        ? ExecutionEnvelope.Write(runDir, envelope, delegated)
                        : ExecutionEnvelope.Write(runDir, envelope, EnvelopeGroupHandoff, delegated);
                }
                catch (ExecutionEnvelope.ForbiddenContentException ex)
                {
                    // The message names a key path or a size, never a value.
                    _logger.LogError("Refusing to run {RunId}: {Message}", job.RunId, ex.Message);
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.RuntimeStartFailed,
                        ex.Message, null, 0, null, null, null, null, false).ConfigureAwait(false);
                    return Disposition.Complete;
                }
                catch (ExecutionEnvelope.HandoffException ex)
                {
                    // The host's fault, not the function's — reported as such. The startup guard
                    // normally stops a host like this claiming work at all; this is the run that
                    // raced it.
                    _logger.LogError("Refusing to run {RunId}: {Message}", job.RunId, ex.Message);
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.SandboxStartFailed,
                        "the sandbox host could not deliver the execution envelope securely",
                        null, 0, null, null, null, null, false).ConfigureAwait(false);
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
                        Redact(result.HostFailure, resolvedValues), result.ExitCode, result.DurationMs, null, null, null, null, false)
                        .ConfigureAwait(false);
                    return Disposition.Complete;
                }

                var (status, errorCode, errorMessage) = RunOutcome.Map(
                    result.OomKilled, result.ExitCode, result.TimedOut, lease.CancelRequested, result.Output);

                // The bootstrap already masks these values in everything it writes; this is the
                // runner's own copy of that rule for the one string it forwards verbatim, so a
                // message that reached it unmasked (a crash before redaction was armed, a line
                // the bootstrap did not write) is still not stored with a value in it.
                errorMessage = Redact(errorMessage, resolvedValues);

                // What this run actually cost, fed back into admission. Reserving a run's whole
                // limit is safe and wasteful; the budget narrows that to the observed p95 only
                // because every completed run reports its peak here.
                _budget.Observe(result.PeakMemoryBytes);

                await CompleteAsync(
                    job, runKey, startedAt, status, errorCode, errorMessage, result.ExitCode, result.DurationMs,
                    result.PeakMemoryBytes, result.CpuUsageMs, result.Output.ResultJson,
                    WithFailureLine(result.Output, errorCode, errorMessage, resolvedValues),
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
        /// The run's logs, plus — for a failed run — one closing <c>error</c> line with everything
        /// the sandbox said about the failure: the error's type and code, its stack, its diagnostic
        /// properties and its whole cause chain. The run record keeps only a one-line message, and
        /// the stack used to be parsed and then dropped, so the Logs panel said "no logs" for the
        /// very run that most needed them. Masked like every other forwarded string, and added
        /// even past the log budget: it is one line, and it is the one that explains the rest.
        /// </summary>
        internal static List<string> WithFailureLine(
            SandboxOutput output, string? errorCode, string? errorMessage, IReadOnlyList<string> secrets)
        {
            if (output.Ok != false || (string.IsNullOrEmpty(output.ErrorStack) && string.IsNullOrEmpty(errorMessage)))
            {
                return output.Logs;
            }

            var text = Redact(string.IsNullOrEmpty(output.ErrorStack) ? errorMessage : output.ErrorStack, secrets);
            var line = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["t"] = "log",
                ["ts"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["level"] = "error",
                ["msg"] = text,
                ["data"] = new Dictionary<string, string?>
                {
                    ["errorCode"] = errorCode,
                    ["message"] = errorMessage,
                },
            });
            return [.. output.Logs, line];
        }

        private readonly record struct Prepared(
            string? Envelope, (string Code, string Message)? Failure, IReadOnlyList<string> Values);

        /// <summary>The bootstrap's marker and threshold (runtime/protocol.mjs), mirrored.</summary>
        private const string Redacted = "[redacted]";

        private const int MinRedactChars = 4;

        /// <summary>
        /// Masks each resolved value in <paramref name="text"/>, longest first so a value that
        /// contains another is masked whole. The same rule the bootstrap applies.
        /// </summary>
        internal static string? Redact(string? text, IReadOnlyList<string> values)
        {
            if (string.IsNullOrEmpty(text) || values.Count == 0) return text;
            foreach (var value in values.Where(v => v.Length >= MinRedactChars).OrderByDescending(v => v.Length))
            {
                text = text.Replace(value, Redacted, StringComparison.Ordinal);
            }
            return text;
        }

        /// <summary>
        /// Resolves every <c>{{secret.&lt;id&gt;}}</c> in the envelope's <c>env</c> in one lookup
        /// and returns the envelope with the values substituted — or the failure to report
        /// instead, in which case the sandbox is never started.
        /// <para>
        /// Two failures, deliberately distinct: a reference that cannot be satisfied is
        /// <see cref="ErrorCodes.SecretUnresolved"/> (not retryable — the author has to fix it)
        /// and names every broken variable by key and secret id; a store that could not be
        /// reached is <see cref="ErrorCodes.SecretStoreUnavailable"/>, which the control plane
        /// retries. Neither message, nor anything logged here, carries a value.
        /// </para>
        /// </summary>
        private async Task<Prepared> ResolveSecretsAsync(RunJob job, string envelope, CancellationToken token)
        {
            var plan = EnvSecretReferences.Collect(envelope);
            if (plan.IsEmpty) return new Prepared(envelope, null, []);

            if (string.IsNullOrWhiteSpace(job.TenantId) ||
                (plan.Caller.TenantId is { } envelopeTenant && !string.Equals(envelopeTenant, job.TenantId, StringComparison.Ordinal)))
            {
                // The entry and the envelope must agree on whose secrets these are. They always do
                // unless something upstream is broken, and then resolving would read one tenant's
                // secrets into another tenant's run.
                _logger.LogError(
                    "Refusing to resolve secrets for run {RunId}: the entry's tenant '{EntryTenant}' does not match the envelope's",
                    job.RunId, job.TenantId);
                return new Prepared(null, (ErrorCodes.SecretUnresolved,
                    "the run's secret references could not be resolved: the run carries no consistent tenant"), []);
            }

            SecretLookup lookup;
            try
            {
                lookup = await _secrets.ResolveAsync(job.TenantId!, plan.Caller, plan.Ids, token).ConfigureAwait(false);
            }
            catch (SecretStoreUnavailableException ex)
            {
                // ex.Message is the resolver's own wording; the inner exception is not logged.
                _logger.LogWarning("Run {RunId} not started: {Message}", job.RunId, ex.Message);
                return new Prepared(null, (ErrorCodes.SecretStoreUnavailable,
                    $"the secrets this function's variables reference could not be fetched ({ex.Message}); " +
                    "nothing ran, and the run is retried if its policy allows"), []);
            }

            var broken = new List<string>();
            foreach (var (key, ids) in plan.IdsByKey)
            {
                foreach (var id in ids.Distinct(StringComparer.Ordinal))
                {
                    if (lookup.Values.ContainsKey(id)) continue;
                    var reason = lookup.Unresolved.TryGetValue(id, out var r) ? r : SecretUnresolvedReasons.NotFound;
                    broken.Add($"variable {key} references secret '{id}', but {reason}");
                }
            }

            if (broken.Count > 0)
            {
                var message = string.Join("; ", broken);
                _logger.LogWarning("Run {RunId} not started: {Message}", job.RunId, message);
                return new Prepared(null, (ErrorCodes.SecretUnresolved, message), []);
            }

            var values = plan.Ids.Select(id => lookup.Values[id]).Distinct(StringComparer.Ordinal).ToArray();
            return new Prepared(EnvSecretReferences.Apply(envelope, plan, lookup.Values), null, values);
        }

        /// <summary>
        /// The caller's delegated Blocks access token for this attempt, or null — in which case
        /// the function sees <c>ctx.blocks.accessToken</c> as <c>undefined</c> and the run goes on.
        /// A token is a convenience the function may use, not a precondition of running it, so
        /// nothing here fails the run.
        /// <para>
        /// The control plane only records a grant for an authenticated caller on a non-public
        /// trigger. That is checked again here against the envelope it actually sent, and the
        /// entry's tenant must be the envelope's: redeeming against the wrong tenant would hand
        /// one tenant's run a token for another.
        /// </para>
        /// </summary>
        private async Task<string?> RedeemAccessTokenAsync(
            RunJob job, Dictionary<string, string> fields, string envelope, CancellationToken token)
        {
            if (!fields.TryGetValue(RedisKeys.RunDelegationField, out var grantId) || string.IsNullOrWhiteSpace(grantId))
            {
                return null;
            }

            var caller = RunDelegation.ReadCaller(envelope);
            if (!caller.IsAuthenticated || caller.UserId is null)
            {
                _logger.LogWarning(
                    "Run {RunId} carries a delegation grant but no authenticated caller; not redeeming it", job.RunId);
                return null;
            }

            if (string.IsNullOrWhiteSpace(job.TenantId)
                || !string.Equals(caller.TenantId, job.TenantId, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Refusing to redeem the delegation grant of run {RunId}: the entry's tenant '{EntryTenant}' does not match the envelope's",
                    job.RunId, job.TenantId);
                return null;
            }

            var accessToken = await _accessTokens.RedeemAsync(job.TenantId, grantId, token).ConfigureAwait(false);
            if (accessToken is null)
            {
                _logger.LogWarning(
                    "Run {RunId} starts without ctx.blocks.accessToken: its delegation grant could not be redeemed", job.RunId);
            }
            return accessToken;
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

        /// <summary>
        /// Updates the status on the run's payload — only while the payload exists. A plain HSET
        /// on a key that has gone (expired, or withdrawn by the control plane because the function
        /// was deleted) would create a two-field hash with no TTL, which nothing ever removes.
        /// </summary>
        private Task<RedisResult> SetStatusAsync(string runKey, string status)
            => _db.ScriptEvaluateAsync(SetStatusIfExistsScript, [runKey],
                [status, DateTimeOffset.UtcNow.ToString("O")]);

        private const string SetStatusIfExistsScript =
            "if redis.call('EXISTS', KEYS[1]) == 1 then " +
            "return redis.call('HSET', KEYS[1], 'status', ARGV[1], 'statusAt', ARGV[2]) end " +
            "return 0";

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
