using System.Text.Json;
using System.Text.Json.Nodes;
using Blocks.Genesis;
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
    /// Sweeps <c>functions:retries</c> (a sorted set scored by due epoch-second) and re-enqueues
    /// a failed run's next attempt once its backoff delay has elapsed —
    /// <see cref="FunctionResultConsumer"/> is what schedules an entry here, on a retryable
    /// failure with attempts remaining.
    /// <para>
    /// A retry reruns the <b>exact envelope</b> the original attempt built — same input, same
    /// caller identity, same configuration, secret-bound variables still as their
    /// <c>{{secret.&lt;id&gt;}}</c> references (the runner resolves them again for this attempt,
    /// so no plaintext is ever copied here) — with only <c>run.attempt</c> patched to
    /// the new number, read back from <c>function:run:{runId}</c> (which is still inside its
    /// 24 h TTL for any retry delay this short). This is why retries only apply to a run with a
    /// deployed version behind it: a Test run's envelope has nowhere durable to be reproduced
    /// from, and re-testing manually is one click away regardless.
    /// </para>
    /// <para>
    /// The claim is a lease, not a removal. A due entry is moved <see cref="ClaimLease"/> into the
    /// future in one script, so of two Workers racing it exactly one claims it, and it is removed
    /// only once handled. Removing it first (as this did) lost the retry for good when the Worker
    /// stopped, or Mongo or Redis failed, before the re-enqueue: the run stayed FAILED and nothing
    /// ever looked at it again, since the stale-run sweeper does not touch finished runs (FN-16).
    /// Now an unfinished claim simply falls due again. Handling it twice is safe: the run's attempt
    /// number makes a second pass a no-op once the first one reset it.
    /// </para>
    /// <para>
    /// One window is left: a crash after the reset but before the stream write leaves the run
    /// QUEUED with nothing queued. The next pass sees the attempt already advanced and lets go; the
    /// stale-run sweeper then closes the run as abandoned — visible, not silent.
    /// </para>
    /// </summary>
    public sealed class FunctionRetryScheduler : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);
        private const int BatchSize = 50;

        /// <summary>How long a claimed entry is held before another pass may take it again.</summary>
        internal static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

        /// <summary>Claims after which an entry that never completes is dropped, with an error.</summary>
        internal const int MaxClaims = 5;

        /// <summary>
        /// Claims a due member: only if it is still in the set and due, move it a lease into the
        /// future and count the claim. Returns the claim count, or 0 when it was not ours to take.
        /// </summary>
        internal const string ClaimScript = @"
local score = redis.call('ZSCORE', KEYS[1], ARGV[1])
if (not score) or tonumber(score) > tonumber(ARGV[2]) then return 0 end
redis.call('ZADD', KEYS[1], 'XX', ARGV[3], ARGV[1])
local claims = redis.call('HINCRBY', KEYS[2], ARGV[1], 1)
redis.call('EXPIRE', KEYS[2], ARGV[4])
return claims";

        /// <summary>Releases a handled member: off the queue and out of the claim count together.</summary>
        internal const string DoneScript = @"
redis.call('ZREM', KEYS[1], ARGV[1])
redis.call('HDEL', KEYS[2], ARGV[1])
return 1";

        private static readonly TimeSpan ClaimCountTtl = TimeSpan.FromDays(1);

        /// <summary>
        /// The entry is written by <see cref="FunctionResultConsumer"/> from an anonymous object,
        /// so its names are camelCase (<c>runId</c>, <c>tenantId</c>…). Read case-sensitively into
        /// <see cref="RetryEntry"/>'s PascalCase properties, every field came back null and every
        /// retry was discarded as "missing required fields" — web defaults match them.
        /// </summary>
        private static readonly JsonSerializerOptions EntryJson = new(JsonSerializerDefaults.Web);

        private readonly IDatabase _db;
        private readonly IFunctionRunRepository _runRepository;
        private readonly IFunctionVersionRepository _versionRepository;
        private readonly ILogger<FunctionRetryScheduler> _logger;
        private readonly Storage.IFunctionArtifactStore? _artifacts;

        public FunctionRetryScheduler(
            ICacheClient cache,
            IFunctionRunRepository runRepository,
            IFunctionVersionRepository versionRepository,
            ILogger<FunctionRetryScheduler> logger,
            Storage.IFunctionArtifactStore? artifacts = null)
        {
            _db = cache.CacheDatabase();
            _runRepository = runRepository;
            _versionRepository = versionRepository;
            _logger = logger;
            _artifacts = artifacts;
        }

        /// <summary>
        /// The artifact URL and hash for a retry of an artifact-built version, signed now (a retry can
        /// come long after the first attempt's URL expired), or none — a version built the registry
        /// way, an artifact gone from the store, or storage that cannot sign. With none, the runner
        /// uses the image reference, exactly as a first attempt does in the same situation.
        /// </summary>
        private async Task<NameValueEntry[]> ArtifactFieldsAsync(string tenantId, Entities.FunctionVersionEntity version, string runId)
        {
            if (_artifacts is null || string.IsNullOrEmpty(version.ArtifactId)) return [];

            string? url;
            try
            {
                url = await _artifacts.CreateDownloadUrlAsync(
                    tenantId, version.ArtifactId, FunctionInvocationService.ArtifactDownloadWindow, CancellationToken.None);
            }
            catch (Storage.FunctionArtifactStoreUnavailableException ex)
            {
                _logger.LogError("Retry for run {RunId} falls back to the image reference: {Reason}", runId, ex.Message);
                return [];
            }

            if (string.IsNullOrEmpty(url))
            {
                _logger.LogError(
                    "Artifact {ArtifactId} for retry of run {RunId} is missing from the store; falling back to the image reference",
                    version.ArtifactId, runId);
                return [];
            }

            return
            [
                new NameValueEntry(FunctionQueueKeys.RunArtifactUrlField, url),
                new NameValueEntry(FunctionQueueKeys.RunArtifactSha256Field, version.ArtifactSha256 ?? string.Empty),
            ];
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

                var claims = await ClaimAsync(member, now);
                if (claims == 0) continue;

                if (claims > MaxClaims)
                {
                    _logger.LogError(
                        "Giving up on a retry claimed {Claims} times without completing; the run keeps its last result: {Member}",
                        claims - 1, (string?)member);
                    await DoneAsync(member);
                    continue;
                }

                try
                {
                    await ProcessDueEntryAsync(member!, cancellationToken);
                    await DoneAsync(member);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Still claimed, so it falls due again after the lease rather than being lost.
                    _logger.LogWarning(ex,
                        "Retry entry not handled (claim {Claims} of {Max}); trying again in {Lease}",
                        claims, MaxClaims, ClaimLease);
                }
            }
        }

        private async Task<long> ClaimAsync(RedisValue member, long now)
        {
            var result = await _db.ScriptEvaluateAsync(ClaimScript,
                [FunctionQueueKeys.RetryQueue, FunctionQueueKeys.RetryClaims],
                [member, now, now + (long)ClaimLease.TotalSeconds, (long)ClaimCountTtl.TotalSeconds]);
            return result.IsNull ? 0 : (long)result;
        }

        private Task DoneAsync(RedisValue member) =>
            _db.ScriptEvaluateAsync(DoneScript,
                [FunctionQueueKeys.RetryQueue, FunctionQueueKeys.RetryClaims], [member]);

        private async Task ProcessDueEntryAsync(string member, CancellationToken cancellationToken)
        {
            RetryEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<RetryEntry>(member, EntryJson);
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
            if (version is null)
            {
                _logger.LogWarning(
                    "Retry for run {RunId} skipped: version '{VersionId}' is no longer available", entry.RunId, entry.VersionId);
                return;
            }

            // The same image and artifact fields the first attempt was queued with
            // (FunctionInvocationService.EnqueueAsync): an artifact-built version has no digest, so
            // its run image is the local name the runner builds from the artifact, and the entry
            // carries a freshly signed URL to build it from on a host that does not have it yet.
            // Worked out before the run is reset, so a version that cannot run leaves the run as it
            // finished rather than QUEUED with nothing queued.
            var image = FunctionRunImage.For(version);
            if (string.IsNullOrEmpty(image))
            {
                _logger.LogWarning(
                    "Retry for run {RunId} skipped: version '{VersionId}' has neither an image nor an artifact",
                    entry.RunId, entry.VersionId);
                return;
            }
            var artifactFields = await ArtifactFieldsAsync(entry.TenantId, version, entry.RunId);

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

            // Conditional: only from the attempt before, and only once it has finished. False means
            // another Worker already advanced it (or something else moved it on) — enqueueing now
            // would run the same attempt twice.
            if (!await _runRepository.ResetForRetryAsync(entry.TenantId, entry.RunId, entry.Attempt, cancellationToken))
            {
                _logger.LogInformation(
                    "Retry for run {RunId} skipped: attempt {Attempt} was already started, or the run moved on",
                    entry.RunId, entry.Attempt);
                return;
            }

            // From here the record says QUEUED, so the enqueue must either land or be undone —
            // never abandoned half-way by a shutdown.
            try
            {
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
                    new NameValueEntry("image", image),
                    new NameValueEntry("attempt", entry.Attempt),
                    .. artifactFields,
                    // The reused envelope still holds references only, so the runner resolves
                    // them afresh for this attempt — a secret rotated since is picked up.
                    new NameValueEntry("protocol", FunctionQueueKeys.RunProtocolVersion),
                    // The first attempt's warm-sandbox opt-in, carried over (only when it had it,
                    // so every other retry entry is unchanged).
                    .. (run.ReuseRequested
                        ? new[] { new NameValueEntry(FunctionQueueKeys.RunReuseField, "1") }
                        : []),
                ]);
                StreamWakeup.Publish(_db, FunctionQueueKeys.RunsNudgeChannel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not re-enqueue run {RunId} for attempt {Attempt}", entry.RunId, entry.Attempt);
                try
                {
                    // Withdraw the payload so a stream entry that did land despite the error
                    // finds nothing to run (the runner discards an entry with no run record).
                    await _db.KeyDeleteAsync(runKey);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning("Could not withdraw the payload of run {RunId}: {Message}", entry.RunId, deleteEx.Message);
                }
                try
                {
                    // Closed rather than left QUEUED with nothing on the queue behind it.
                    await _runRepository.FailIfNotTerminalAsync(
                        entry.TenantId, entry.RunId, RunErrorCode.EnqueueFailed,
                        $"retry attempt {entry.Attempt} could not be queued ({ex.GetType().Name}); nothing was executed",
                        DateTime.UtcNow, CancellationToken.None);
                }
                catch (Exception compensationEx)
                {
                    // The stale-run sweeper closes it later instead.
                    _logger.LogError(compensationEx,
                        "Could not close run {RunId} after its retry failed to enqueue; the stale-run sweeper will", entry.RunId);
                }
                return;
            }

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
