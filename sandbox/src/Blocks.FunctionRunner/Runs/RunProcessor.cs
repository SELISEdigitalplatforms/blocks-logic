using System.Diagnostics;
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
        private readonly SecretStore.ISecretStoreBreaker? _secretBreaker;
        private readonly IRunSecretResolver _secrets;
        private readonly IRunAccessTokenResolver _accessTokens;
        private readonly WarmPool? _warm;
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
            Maintenance.IImageUsageLog? usage = null,
            SecretStore.ISecretStoreBreaker? secretBreaker = null,
            WarmPool? warmPool = null)
        {
            _db = db;
            _sandbox = sandbox;
            _images = images;
            _budget = budget;
            _usage = usage;
            _secretBreaker = secretBreaker;
            _secrets = secrets;
            _accessTokens = accessTokens;
            _options = options.Value;
            _logger = logger;
            _warm = warmPool;
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

            // Stamped before the image, because producing it is part of how long this run took
            // from the caller's point of view even though it happens before admission.
            var startedAt = DateTimeOffset.UtcNow;

            // The claim, for a warm run's handoverMs (claim → envelope on the sandbox's stdin).
            var claimed = Stopwatch.StartNew();
            var timings = new HandoverTimings(claimed);

            // --- image, before any slot is taken ------------------------------------
            // Producing the image can mean downloading an artifact and building it, which is
            // seconds. Doing that while holding a host slot, a tenant slot and one of the
            // function's own meant a cold function paid for capacity it was not yet using — and on
            // a busy host that is capacity the deployed version wanted. Nothing here needs a slot:
            // a slot is for running. The work is also cached and idempotent, so preparing for a run
            // that is then deferred costs nothing the next attempt does not reuse.
            var image = await _images
                .EnsureAsync(job.Image, token, job.ArtifactUrl, job.ArtifactSha256)
                .ConfigureAwait(false);

            // What the cache evicts by. Stamped on resolve rather than on completion so a run that
            // fails still counts as use — the image was wanted, which is the question the cache is
            // asking.
            if (image is not null) _usage?.Touch(image);
            timings.ImageReady();
            if (image is null)
            {
                await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.ImagePullFailed,
                    $"the image '{job.Image}' could not be resolved", null, 0, null, null, null, null, false)
                    .ConfigureAwait(false);
                return Disposition.Complete;
            }

            // Asked before any slot is taken, which is the whole point: a store that is known to
            // be down should cost nothing at all, rather than three slots per run for the length
            // of a timeout. Resolution itself stays where it is, as late as possible.
            if (_secretBreaker?.ShouldSkip(job.TenantId) == true)
            {
                _logger.LogDebug(
                    "Secret store for tenant {TenantId} is being left alone; deferring run {RunId}",
                    job.TenantId, job.RunId);
                return Disposition.Deferred;
            }

            // A test yields to real traffic. It is a developer convenience and the deployed
            // version is somebody's customer, so on a busy host the test waits — on the queue, like
            // everything else that cannot run yet. Checked before the reservation so a waiting test
            // holds nothing at all.
            if (job.IsTest && _budget.Capacity > 0)
            {
                var usedPercent = _budget.Active * 100 / _budget.Capacity;
                if (usedPercent >= _options.TestDeferAbovePercent)
                {
                    _logger.LogDebug(
                        "Host is {Used}% busy; deferring test run {RunId} so deployed versions keep the room",
                        usedPercent, job.RunId);
                    return Disposition.Deferred;
                }
            }

            // Served by a warm sandbox only when both sides asked: the host (SandboxReuse) and the
            // function (reuse=1 on its entry). Never a test: a test's image is local and deleted
            // when it ends, which a sandbox kept alive on it would prevent.
            var useWarm = _options.SandboxReuse && job.Reuse && !job.IsTest
                && _warm is not null && _warm.SupportsReuse(image);

            // Admission. Neither refusal is a failure: the entry stays pending and is retried.
            // A warm call takes a slot only — the sandbox's memory and its start are charged by the
            // pool, for as long as the sandbox lives, not per call.
            using var reservation = new Admission(useWarm ? _budget.TryReserveSlot() : await ReserveSingleAsync(limits).ConfigureAwait(false));
            if (reservation.Current is null)
            {
                _logger.LogDebug("Host is at capacity ({Active}/{Capacity}); deferring run {RunId}",
                    _budget.Active, _budget.Capacity, job.RunId);
                return Disposition.Deferred;
            }

            // A tenant's share of the fleet, and the function's own limit. The host budget above is
            // first-come-first-served, so without the tenant gate one tenant's burst could hold
            // every slot on a runner and every other tenant waited behind it. A test draws on its
            // own budget, never the function's: clicking Test must not be able to delay the traffic
            // that function's deployed version is serving.
            //
            // Both are taken at once — two independent keys, pipelined into one Redis round trip
            // instead of two (each is a remote call on the hot path of every run). Whichever was
            // taken is released by its `await using` when the other refuses, so the outcome is
            // the same as taking them one after the other.
            var tenantSlotTask = string.IsNullOrEmpty(job.TenantId)
                ? Task.FromResult<FunctionConcurrency?>(null)
                : FunctionConcurrency.TryEnterTenantAsync(
                    _db, job.TenantId, job.RunId, _options.TenantSlotLimit(_budget.Capacity));
            var slotTask = job.IsTest
                ? FunctionConcurrency.TryEnterTestAsync(_db, job.FunctionId, job.RunId)
                : FunctionConcurrency.TryEnterAsync(_db, job.FunctionId, job.RunId, limits.FunctionConcurrency);
            await Task.WhenAll(
                tenantSlotTask.ContinueWith(static _ => { }, TaskScheduler.Default),
                slotTask.ContinueWith(static _ => { }, TaskScheduler.Default)).ConfigureAwait(false);
            // Both settled, so a fault in one cannot leave the other's slot held: register both
            // for release before either result is read (a faulted one rethrows on its own await).
            await using var tenantSlot = tenantSlotTask.IsCompletedSuccessfully ? tenantSlotTask.Result : null;
            await using var slot = slotTask.IsCompletedSuccessfully ? slotTask.Result : null;
            await tenantSlotTask.ConfigureAwait(false);
            await slotTask.ConfigureAwait(false);

            if (tenantSlot is null && !string.IsNullOrEmpty(job.TenantId))
            {
                _logger.LogDebug(
                    "Tenant {TenantId} is at its share of {Limit} sandbox slot(s); deferring run {RunId}",
                    job.TenantId, _options.TenantSlotLimit(_budget.Capacity), job.RunId);
                return Disposition.Deferred;
            }

            if (slot is null)
            {
                _logger.LogDebug("Function {FunctionId} is at its {Kind} concurrency limit; deferring run {RunId}",
                    job.FunctionId, job.IsTest ? "test" : "function", job.RunId);
                return Disposition.Deferred;
            }

            // After the slots, not beside them: a run that is only deferred must never hold the
            // lease, or a second runner handed the same entry would read it as someone else's.
            await using var lease = await RunLease.TryAcquireAsync(
                _db, _logger, job.RunId, TimeSpan.FromMilliseconds(_options.LeaseMs), token).ConfigureAwait(false);
            if (lease is null)
            {
                _logger.LogInformation("Run {RunId} is already leased by another runner", job.RunId);
                return Disposition.NotOurs;
            }
            timings.Admitted();

            // A display status for the run list, so not waited for here: it travels on the same
            // connection as the commands after it, and Redis applies one connection's commands in
            // order, so it still lands before Running and before the result.
            _ = SetStatusQuietlyAsync(runKey, RunStatuses.Starting, job.RunId);

            if (useWarm)
            {
                var served = await ProcessWarmAsync(job, fields, runKey, startedAt, claimed, timings, image, limits, lease, token)
                    .ConfigureAwait(false);
                if (served is { } disposition) return disposition;

                // The image turned out to have no reuse runtime (built on 24-v1). That is nobody's
                // failure: this run, and every later one of this image, takes the single-run path.
                // It needs a single run's whole reservation — memory and a start — not just the
                // slot a warm call takes.
                reservation.Swap(await ReserveSingleAsync(limits).ConfigureAwait(false));
                if (reservation.Current is null)
                {
                    _logger.LogDebug("Host is at capacity; deferring run {RunId} after its warm start fell back", job.RunId);
                    return Disposition.Deferred;
                }
            }

            var runDir = Path.Combine(_options.RunsDir, job.RunId);
            try
            {
                // --- envelope ---------------------------------------------------------
                // What comes off the queue carries secret-bound variables as references; the
                // plaintext is fetched here, as late as possible, and exists only in this
                // method's memory and in the 0440 envelope file removed by CleanUp below. It is
                // never written back to the run record: a retry re-reads the references and
                // resolves them afresh, which is also what makes a rotated secret take effect on
                // the very next attempt.
                var prepared = await PrepareEnvelopeAsync(job, fields, runKey, startedAt, token, timings).ConfigureAwait(false);
                if (prepared.Done is { } done) return done;

                string envelopePath;
                var resolvedValues = prepared.Values;
                try
                {
                    envelopePath = EnvelopeGroupHandoff is null
                        ? ExecutionEnvelope.Write(runDir, prepared.Envelope!, prepared.Delegated)
                        : ExecutionEnvelope.Write(runDir, prepared.Envelope!, EnvelopeGroupHandoff, prepared.Delegated);
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

                // From here a container is created: the start charge is spent. Every return above
                // gives it back with the reservation (HostReservation).
                reservation.Current?.ContainerStarted();
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
                        Redact(result.HostFailure, resolvedValues), result.ExitCode, result.DurationMs,
                        null, null, null, null, false,
                        startupMs: result.StartupMs, executionMs: result.ExecutionMs)
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
                    result.Output.Truncated,
                    startupMs: result.StartupMs, executionMs: result.ExecutionMs, timings: timings)
                    .ConfigureAwait(false);

                return Disposition.Complete;
            }
            finally
            {
                CleanUp(runDir);
            }
        }

        /// <summary>The envelope ready to hand over, or the disposition already reached instead.</summary>
        private sealed record PreparedRun(
            string? Envelope, IReadOnlyList<string> Values, bool Delegated, Disposition? Done);

        /// <summary>
        /// Everything between the run record and the sandbox, shared by both paths so a warm call
        /// gets exactly the envelope a single run would: screened, secrets resolved, the delegated
        /// token redeemed for this run. When the run cannot go ahead the result is already reported
        /// (or the run deferred) and <see cref="PreparedRun.Done"/> says how to dispose of the entry.
        /// </summary>
        private async Task<PreparedRun> PrepareEnvelopeAsync(
            RunJob job, Dictionary<string, string> fields, string runKey, DateTimeOffset startedAt, CancellationToken token,
            HandoverTimings? timings = null)
        {
            IReadOnlyList<string> resolvedValues = [];
            Task<string?>? tokenTask = null;
            try
            {
                var envelope = fields.TryGetValue("envelope", out var e) ? e : "{}";

                // Screened before anything is resolved, so an envelope the platform would
                // refuse anyway never causes a secret to be read.
                ExecutionEnvelope.Screen(envelope);

                // The caller's token is redeemed while the secrets resolve, not after them: two
                // independent remote calls (IAM, and the secret store), each a few hundred ms, that
                // used to be paid one after the other on every run. After the screen, deliberately:
                // it refuses an `accessToken` key, and this one is added by the runner from a grant,
                // never carried by the queue. The caller is read from the envelope as queued —
                // resolving secrets changes variables, never the caller.
                //
                // A redemption is not single-use (every attempt redeems afresh), so one made for a
                // run that is then deferred or failed costs one IAM call and nothing else; its
                // token is dropped unread.
                var queuedEnvelope = envelope;
                tokenTask = Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    try { return await RedeemAccessTokenAsync(job, fields, queuedEnvelope, token).ConfigureAwait(false); }
                    finally { timings?.Token(sw.ElapsedMilliseconds); }
                }, CancellationToken.None);

                if (job.Protocol >= RedisKeys.RunProtocolVersion)
                {
                    var secretsWatch = Stopwatch.StartNew();
                    var prepared = await ResolveSecretsAsync(job, envelope, token).ConfigureAwait(false);
                    timings?.Secrets(secretsWatch.ElapsedMilliseconds);
                    if (prepared.Failure is { } failure)
                    {
                        // An unreachable store is not this function's failure — nothing ran, and
                        // the code was never at fault. Everything else here that cannot run yet
                        // is deferred: a full host, a tenant over its share, a function at its
                        // limit. This is the same thing, so it gets the same answer.
                        //
                        // Failing instead spent one of the run's attempts, and a second blip
                        // then reported a permanently failed run to a tenant whose function was
                        // fine. The breaker below keeps the deferred runs from taking slots
                        // while they wait.
                        if (failure.Code == ErrorCodes.SecretStoreUnavailable)
                        {
                            _secretBreaker?.RecordUnavailable(job.TenantId);

                            // Deferring suits a deployed run: nobody is waiting, the entry keeps
                            // its place on the stream, and it runs when the store is back.
                            //
                            // A test is the opposite. Someone is watching it, and the test loop
                            // gives admission a bounded two minutes — so deferring there just
                            // spins until that runs out and then reports "no sandbox slot freed
                            // up", which is not what happened. The person waiting is better
                            // served by the real reason, straight away.
                            if (!job.IsTest)
                            {
                                _logger.LogWarning(
                                    "Run {RunId} cannot start: the secret store is unreachable. Leaving it "
                                    + "queued rather than failing it.", job.RunId);
                                return new PreparedRun(null, [], false, Disposition.Deferred);
                            }
                        }

                        // A secret that does not exist, or that this caller may not read, is the
                        // author's to fix. Retrying it changes nothing, so it stays a failure.
                        _secretBreaker?.RecordSuccess(job.TenantId);
                        await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, failure.Code,
                            failure.Message, null, 0, null, null, null, null, false).ConfigureAwait(false);
                        return new PreparedRun(null, [], false, Disposition.Complete);
                    }

                    _secretBreaker?.RecordSuccess(job.TenantId);
                    envelope = prepared.Envelope!;
                    resolvedValues = prepared.Values;
                }

                var accessToken = await tokenTask.ConfigureAwait(false);
                tokenTask = null;
                if (accessToken is not null)
                {
                    envelope = RunDelegation.Apply(envelope, accessToken);
                    resolvedValues = [.. resolvedValues, accessToken];
                }

                return new PreparedRun(envelope, resolvedValues, accessToken is not null, null);
            }
            catch (ExecutionEnvelope.ForbiddenContentException ex)
            {
                // The message names a key path or a size, never a value.
                _logger.LogError("Refusing to run {RunId}: {Message}", job.RunId, ex.Message);
                await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.RuntimeStartFailed,
                    ex.Message, null, 0, null, null, null, null, false).ConfigureAwait(false);
                return new PreparedRun(null, [], false, Disposition.Complete);
            }
            finally
            {
                // Any path that did not use the token leaves it to finish on its own, its result
                // (and any fault) observed and dropped — never awaited, so a slow IAM cannot hold
                // a run that has already been deferred or failed.
                if (tokenTask is not null)
                {
                    _ = tokenTask.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                }
            }
        }

        /// <summary>
        /// Serves the run from the warm pool (sandbox/REUSE.md). Null means the image cannot do
        /// reuse and the caller must take the single-run path; anything else is final.
        /// <para>
        /// The order differs from the single path in one place, on purpose: the sandbox is acquired
        /// <i>before</i> the envelope is prepared. Starting one can be refused (no memory, the start
        /// rate), and a refusal must come before a delegation grant is redeemed or a secret read for
        /// a run that is then only deferred.
        /// </para>
        /// </summary>
        private async Task<Disposition?> ProcessWarmAsync(
            RunJob job, Dictionary<string, string> fields, string runKey, DateTimeOffset startedAt,
            Stopwatch claimed, HandoverTimings timings, string image, RunLimits limits, RunLease lease, CancellationToken token)
        {
            var key = new WarmKey(job.TenantId ?? string.Empty, job.FunctionId, job.VersionId ?? string.Empty, image);

            var (acquired, early) = await AcquireWarmAsync(job, runKey, startedAt, claimed, key, limits, lease, allowIdle: true, token)
                .ConfigureAwait(false);
            if (acquired is null) return early;

            var handle = acquired;
            timings.SandboxReady();
            WarmCallResult call;
            IReadOnlyList<string> resolvedValues;
            try
            {
                var prepared = await PrepareEnvelopeAsync(job, fields, runKey, startedAt, token, timings).ConfigureAwait(false);
                if (prepared.Done is { } done)
                {
                    await _warm!.ReturnUnusedAsync(handle).ConfigureAwait(false);
                    return done;
                }

                string line;
                try
                {
                    line = ExecutionEnvelope.ToLine(prepared.Envelope!, prepared.Delegated);
                }
                catch (ExecutionEnvelope.ForbiddenContentException ex)
                {
                    await _warm!.ReturnUnusedAsync(handle).ConfigureAwait(false);
                    _logger.LogError("Refusing to run {RunId}: {Message}", job.RunId, ex.Message);
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.RuntimeStartFailed,
                        ex.Message, null, 0, null, null, null, null, false).ConfigureAwait(false);
                    return Disposition.Complete;
                }

                resolvedValues = prepared.Values;
                // Display only, and ordered ahead of the result on the same connection (see Starting).
                _ = SetStatusQuietlyAsync(runKey, RunStatuses.Running, job.RunId);
                call = await handle.RunCallAsync(job.RunId, line, limits, claimed, lease.Token).ConfigureAwait(false);

                // A sandbox from the pool that died before this call's `started` line — it would
                // not resume, would not take the envelope, or exited on the spot — failed nobody's
                // code: whatever killed it happened in its previous life. The run is served once
                // more, on a fresh sandbox. Only after `started` is a failure the call's own.
                if (!call.Started && handle.Reused && call.Discard is not null and not "cancelled"
                    && !lease.Token.IsCancellationRequested)
                {
                    var lost = await _warm!.ReleaseAsync(handle, call).ConfigureAwait(false);
                    _logger.LogWarning(
                        "Warm sandbox for run {RunId} died before the call started ({Reason}); serving it on a fresh one",
                        job.RunId, lost);

                    var (fresh, freshEarly) = await AcquireWarmAsync(
                        job, runKey, startedAt, claimed, key, limits, lease, allowIdle: false, token).ConfigureAwait(false);
                    // No room for a fresh one — or this image cannot do reuse after all: the run
                    // waits for the next attempt rather than taking the single path, which would
                    // redeem its delegation grant a second time.
                    if (fresh is null) return freshEarly is null ? Disposition.Deferred : freshEarly;

                    handle = fresh;
                    call = await handle.RunCallAsync(job.RunId, line, limits, claimed, lease.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // Whatever went wrong, a sandbox that may have seen part of this run is not
                // offered to the next one. A no-op when the handle was already handed back.
                await _warm!.DiscardAsync(handle, "crash").ConfigureAwait(false);
                throw;
            }

            var discard = await _warm!.ReleaseAsync(handle, call).ConfigureAwait(false);
            timings.Log(_logger, job.RunId, call.HandoverMs, handle.Reused);
            var report = new WarmReport(handle.Reused, discard, call.HandoverMs);
            var result = call.Result;

            if (lease.LeaseLost)
            {
                _logger.LogWarning("Abandoning run {RunId} after losing its lease", job.RunId);
                return Disposition.NotOurs;
            }

            if (token.IsCancellationRequested)
            {
                // The runner is shutting down and killed the sandbox under the call. That is not
                // the run's outcome: leave it pending for whichever runner claims it next.
                return Disposition.Deferred;
            }

            if (result.HostFailure is not null)
            {
                await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.SandboxStartFailed,
                    Redact(result.HostFailure, resolvedValues), result.ExitCode, result.DurationMs,
                    null, null, null, null, false,
                    startupMs: result.StartupMs, executionMs: result.ExecutionMs, warm: report)
                    .ConfigureAwait(false);
                return Disposition.Complete;
            }

            var (status, errorCode, errorMessage) = RunOutcome.Map(
                result.OomKilled, result.ExitCode, result.TimedOut, lease.CancelRequested, result.Output);
            errorMessage = Redact(errorMessage, resolvedValues);

            // Not fed to _budget.Observe: a warm call's memory figure is a snapshot of a long-lived
            // sandbox after the call, not the peak of one run, and the footprint estimate is a
            // distribution of single-run peaks. The pool reserves a warm sandbox's memory itself.

            await CompleteAsync(
                job, runKey, startedAt, status, errorCode, errorMessage, result.ExitCode, result.DurationMs,
                result.PeakMemoryBytes, result.CpuUsageMs, result.Output.ResultJson,
                WithFailureLine(result.Output, errorCode, errorMessage, resolvedValues),
                result.Output.Truncated,
                startupMs: result.StartupMs, executionMs: result.ExecutionMs, warm: report, timings: timings)
                .ConfigureAwait(false);

            return Disposition.Complete;
        }

        /// <summary>
        /// A sandbox from the pool, or what to do with the run instead: null with null for an image
        /// that cannot do reuse (take the single path), or the disposition already reached.
        /// </summary>
        private async Task<(WarmHandle? Handle, Disposition? Done)> AcquireWarmAsync(
            RunJob job, string runKey, DateTimeOffset startedAt, Stopwatch claimed, WarmKey key, RunLimits limits,
            RunLease lease, bool allowIdle, CancellationToken token)
        {
            WarmAcquireResult acquired;
            try
            {
                acquired = await _warm!.AcquireAsync(key, limits, lease.Token, allowIdle).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                if (lease.LeaseLost) return (null, Disposition.NotOurs);
                if (lease.CancelRequested)
                {
                    await CompleteAsync(job, runKey, startedAt, RunStatuses.Cancelled, null, "the run was cancelled",
                        null, claimed.ElapsedMilliseconds, null, null, null, null, false,
                        warm: new WarmReport(false, "cancelled", null)).ConfigureAwait(false);
                    return (null, Disposition.Complete);
                }

                // Nobody cancelled anything: an infrastructure timeout. Retryable, so the run waits.
                _logger.LogWarning("Starting a warm sandbox for run {RunId} timed out; deferring it", job.RunId);
                return (null, Disposition.Deferred);
            }

            switch (acquired.Status)
            {
                case WarmAcquireStatus.NoReuseSupport:
                    return (null, null);

                case WarmAcquireStatus.NoCapacity:
                    _logger.LogDebug("No room for a warm sandbox of function {FunctionId}; deferring run {RunId}",
                        job.FunctionId, job.RunId);
                    return (null, Disposition.Deferred);

                case WarmAcquireStatus.StartFailed:
                    return (null, await ReportFailedStartAsync(job, runKey, startedAt, acquired.Start!).ConfigureAwait(false));

                default:
                    return (acquired.Handle!, null);
            }
        }

        /// <summary>
        /// A single run's whole reservation. Refused for memory while idle warm sandboxes hold it,
        /// the least recently used one is given up and the reservation tried once more: a paused
        /// sandbox nobody is calling must not starve a run, or a test, that is waiting.
        /// </summary>
        private async Task<HostReservation?> ReserveSingleAsync(RunLimits limits)
        {
            var reservation = _budget.TryReserve(limits.MemoryBytes, out var refusal);
            if (reservation is null && refusal == AdmissionRefusal.Memory && _warm is not null
                && await _warm.EvictIdleAsync().ConfigureAwait(false))
            {
                reservation = _budget.TryReserve(limits.MemoryBytes, out _);
            }
            return reservation;
        }

        /// <summary>
        /// A warm sandbox that never became ready. A function that does not load fails exactly as a
        /// single run of it would — same status, same code, same logs — because it is the same
        /// fault; a sandbox the host could not create is the host's, as on the single path.
        /// </summary>
        private async Task<Disposition> ReportFailedStartAsync(
            RunJob job, string runKey, DateTimeOffset startedAt, WarmStartResult start)
        {
            var report = new WarmReport(false, "crash", null);

            if (start.Status == WarmStartStatus.HostFailure)
            {
                await CompleteAsync(job, runKey, startedAt, RunStatuses.Failed, ErrorCodes.SandboxStartFailed,
                    start.HostFailure, start.ExitCode, start.StartupMs, null, null, null, null, false,
                    warm: report).ConfigureAwait(false);
                return Disposition.Complete;
            }

            var (status, errorCode, errorMessage) = RunOutcome.Map(
                start.OomKilled, start.ExitCode, start.TimedOut, cancelled: false, start.Output);
            await CompleteAsync(
                job, runKey, startedAt, status, errorCode, errorMessage, start.ExitCode, start.StartupMs,
                null, null, null, WithFailureLine(start.Output, errorCode, errorMessage, []), start.Output.Truncated,
                startupMs: start.StartupMs, warm: report).ConfigureAwait(false);
            return Disposition.Complete;
        }

        /// <summary>The reuse fields of a result entry (sandbox/REUSE.md), present only for a warm run.</summary>
        private sealed record WarmReport(bool Reused, string Discard, long? HandoverMs);

        /// <summary>
        /// The run's host reservation, swappable: a warm run that falls back to the single path
        /// trades its slot for a single run's full reservation without leaving the using scope.
        /// </summary>
        private sealed class Admission(HostReservation? current) : IDisposable
        {
            public HostReservation? Current { get; private set; } = current;

            public void Swap(HostReservation? next)
            {
                Current?.Dispose();
                Current = next;
            }

            public void Dispose() => Current?.Dispose();
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
            string? resultJson, List<string>? logs, bool truncated,
            long? startupMs = null, long? executionMs = null, WarmReport? warm = null, HandoverTimings? timings = null)
        {
            var completedAt = DateTimeOffset.UtcNow;
            string? resultKey = null;
            string? logsKey = null;

            // The result, the logs and the status are written together — sent back to back on the
            // one connection, so Redis applies them in this order, and waited for as one round
            // trip instead of five one after another (each a remote call on the answer's path).
            // All of them land before the stream entry below, as before: a crash in between still
            // replays the run rather than losing it.
            var writes = new List<Task>(5);
            if (resultJson is not null)
            {
                resultKey = RedisKeys.Result(job.RunId);
                writes.Add(_db.StringSetAsync(resultKey, resultJson, RedisKeys.ResultTtl));
            }

            if (logs is { Count: > 0 })
            {
                logsKey = RedisKeys.Logs(job.RunId);
                var values = new RedisValue[logs.Count];
                for (var i = 0; i < logs.Count; i++) values[i] = logs[i];
                writes.Add(_db.KeyDeleteAsync(logsKey));
                writes.Add(_db.ListRightPushAsync(logsKey, values));
                writes.Add(_db.KeyExpireAsync(logsKey, RedisKeys.LogsTtl));
            }

            writes.Add(SetStatusAsync(runKey, status));
            await Task.WhenAll(writes).ConfigureAwait(false);

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
                new("startupMs", startupMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                new("executionMs", executionMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
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

            // A warm run's three extra fields (sandbox/REUSE.md), appended after everything above
            // so the existing fields stay exactly as they were. A single run carries none of them,
            // which a consumer reads the same as reused=0.
            if (warm is not null)
            {
                entry =
                [
                    .. entry,
                    new("reused", warm.Reused ? "1" : "0"),
                    new("discard", warm.Discard),
                    new("handoverMs", warm.HandoverMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                ];
            }

            // Every step time so far (Api, queue, hand-over), for the run record. Optional: a
            // consumer that predates it ignores it.
            var composed = HandoverTimings.Compose(job.ApiTimings, job.QueuedMs, timings, warm?.HandoverMs);
            if (composed.Length > 0)
            {
                entry = [.. entry, new(RedisKeys.ResultTimingsField, composed)];
            }

            await _db.StreamAddAsync(RedisKeys.ResultsStream, entry).ConfigureAwait(false);
            StreamWakeup.Publish(_db, RedisKeys.ResultsNudgeChannel);

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
        /// <summary>
        /// <see cref="SetStatusAsync"/> for a status that is only shown, never decided on: not
        /// awaited by the caller, and a failure is logged rather than thrown, because failing a run
        /// over its display status — after its sandbox may already have the envelope — would run it
        /// twice. Commands on one multiplexed connection reach Redis in the order they were issued,
        /// so a status sent first still lands before anything sent after it.
        /// </summary>
        private async Task SetStatusQuietlyAsync(string runKey, string status, string runId)
        {
            try
            {
                await SetStatusAsync(runKey, status).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not mark run {RunId} {Status} ({ExceptionType})", runId, status, ex.GetType().Name);
            }
        }

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
