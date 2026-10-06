namespace Functions.DomainService.Queue
{
    /// <summary>
    /// Every Redis key and stream name in the runner contract.
    /// <para>
    /// This is the control-plane copy of <c>plan/PROTOCOL.md</c>; the Runner VM carries its own
    /// in <c>Blocks.FunctionRunner.Contracts.RedisKeys</c>. The two are not shared code — the
    /// halves live in different repositories — so <b>any change here is a protocol change and
    /// must be made on both sides at once</b>. A rename that only lands on one side does not
    /// fail loudly; runs simply stop being delivered.
    /// </para>
    /// </summary>
    public static class FunctionQueueKeys
    {
        /// <summary>Carried on every message. Bump only with both halves.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>
        /// Namespace for every key and stream below, so two environments can share one Redis
        /// without sharing work.
        /// <para>
        /// Empty by default, which produces exactly the names this protocol has always used — an
        /// unset deployment is byte-identical to the one before this existed. Set it from
        /// <c>Functions:QueuePrefix</c> (env <c>Functions__QueuePrefix</c>) and the matching
        /// <c>RUNNER__QueuePrefix</c> on every runner that serves it; the two halves must agree,
        /// because a mismatch does not fail loudly — the work is simply never delivered.
        /// </para>
        /// <para>
        /// This exists because the streams were fixed literals, so every runner pointed at a
        /// Redis joined the same consumer group and competed for the same entries. A developer's
        /// test could be claimed and built by someone else's host, which has its own private
        /// image registry and possibly its own build of the runner — and the failure surfaced on
        /// the developer's screen with nothing in their own logs to explain it.
        /// </para>
        /// <para>Consumer groups are not prefixed: they live on the streams, so namespacing the
        /// stream namespaces the group with it.</para>
        /// </summary>
        public static string Prefix
        {
            get => _prefix;
            set => _prefix = Normalize(value);
        }

        private static string _prefix = string.Empty;

        /// <summary>Blank stays blank; anything else gets exactly one trailing colon.</summary>
        internal static string Normalize(string? prefix)
        {
            var trimmed = prefix?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return string.Empty;

            return trimmed.TrimEnd(':') + ":";
        }

        /// <summary>
        /// Version of a <see cref="RunsStream"/> entry, the one message that has moved on: 2 means
        /// the envelope's <c>env</c> carries secret-bound variables as <c>{{secret.&lt;id&gt;}}</c>
        /// references that the runner resolves right before the sandbox starts. A runner that
        /// predates it only accepts 1 and dead-letters anything else, so this control plane in
        /// front of an old runner fails runs as <see cref="Enums.RunErrorCode.Undeliverable"/>
        /// instead of executing a function with reference text where its key should be. Mirrors
        /// <c>RedisKeys.RunProtocolVersion</c> in the runner. Results and builds stay on
        /// <see cref="ProtocolVersion"/>.
        /// </summary>
        public const int RunProtocolVersion = 2;

        /// <summary>
        /// Optional field of the <see cref="Run"/> hash: the Genesis delegation grant id of the
        /// caller who invoked the run, which the runner redeems with IAM right before the sandbox
        /// starts and hands the function as <c>ctx.blocks.accessToken</c>. Written beside the
        /// envelope, never inside it — the envelope is screened for credentials on both sides —
        /// and never a token: the grant id is useless without the tenant's salt. Needs no protocol
        /// bump, because a runner that predates it simply ignores the field and the function sees
        /// no token. Mirrors <c>RedisKeys.RunDelegationField</c> in the runner.
        /// </summary>
        public const string RunDelegationField = "delegation";

        /// <summary>
        /// Optional field of a <see cref="BuildsStream"/> entry: a short-lived, write-only URL for the
        /// one blob this build's artifact belongs in.
        /// <para>
        /// Optional on purpose, and no protocol bump: a runner that predates it ignores the field and
        /// publishes to the registry as it always did, so an old runner in front of this control plane
        /// keeps working. A runner that understands it uploads instead.
        /// </para>
        /// </summary>
        public const string BuildArtifactUploadField = "artifactUploadUrl";

        /// <summary>
        /// Optional fields of a <see cref="RunsStream"/> entry: a short-lived, read-only URL for the
        /// artifact this run needs, and the SHA-256 the runner must find once it has it.
        /// <para>
        /// The hash is what makes the download trustworthy — the URL is signed, but a truncated or
        /// swapped blob would otherwise be built into an image and executed. Same optionality rule as
        /// the upload field above.
        /// </para>
        /// </summary>
        public const string RunArtifactUrlField = "artifactUrl";

        /// <inheritdoc cref="RunArtifactUrlField" />
        public const string RunArtifactSha256Field = "artifactSha256";

        // ---- streams ------------------------------------------------------------
        public static string RunsStream => _prefix + "functions:runs";

        /// <summary>
        /// Pre-warm requests (sandbox/REUSE.md): one entry per deploy of a version whose trigger has
        /// <c>ReuseSandbox</c> — <c>{tenantId, functionId, versionId, image, count, drainVersionId}</c>.
        /// A runner with <c>RUNNER__SandboxReuse</c> starts <c>count</c> warm sandboxes for the
        /// version (best effort, within its host budget) and destroys idle sandboxes of
        /// <c>drainVersionId</c>. Purely advisory: a runner that predates it never reads the stream,
        /// and a lost entry only means the first call pays a cold start. Same prefix rule as
        /// <see cref="RunsStream"/>. Mirrors <c>RedisKeys.WarmStream</c> in the runner.
        /// </summary>
        public static string WarmStream => _prefix + "functions:warm";

        /// <summary>
        /// Optional field of a <see cref="RunsStream"/> entry: <c>"1"</c> when the deployed
        /// version's trigger opted into <c>ReuseSandbox</c>. Absent otherwise — and always absent
        /// on a test, which goes to <see cref="TestsStream"/> — so a run entry from a function
        /// that never opted in is byte-identical to before. A runner without reuse ignores it.
        /// </summary>
        public const string RunReuseField = "reuse";
        public static string ResultsStream => _prefix + "functions:results";

        /// <summary>
        /// Mirrors <c>RedisKeys.RunsNudgeChannel</c> in the runner. Pub/sub wake-ups for <see cref="RunsStream"/> and <see cref="ResultsStream"/>: the writer
        /// publishes one right after its XADD, so an idle reader reads at once instead of on its
        /// next 250 ms poll (StackExchange.Redis cannot block on XREADGROUP). Advisory only — the
        /// poll stays, so a lost or unheard nudge costs exactly the old latency, and a side that
        /// predates them neither sends nor listens. The payload means nothing.
        /// </summary>
        public static string RunsNudgeChannel => _prefix + "functions:runs:nudge";

        /// <inheritdoc cref="RunsNudgeChannel" />
        public static string ResultsNudgeChannel => _prefix + "functions:results:nudge";
        public static string BuildsStream => _prefix + "functions:builds";
        public static string BuildResultsStream => _prefix + "functions:build-results";

        /// <summary>
        /// Sorted set of base images live runners build on (member = reference, score = unix seconds
        /// last announced). Mirrors <c>RedisKeys.BaseImages</c>; read by the build cache.
        /// </summary>
        public static string BaseImages => _prefix + "functions:base-images";

        /// <summary>Build result field: the base image a build was made FROM.</summary>
        public const string BuildBaseImageField = "baseImage";

        /// <summary>
        /// Test runs: one entry builds the current source on the runner that claims it, runs it
        /// there once and deletes the image. Mirrors <c>RedisKeys.TestsStream</c>. Separate from
        /// <see cref="RunsStream"/> so a runner that predates it never takes one.
        /// </summary>
        public static string TestsStream => _prefix + "functions:tests";
        public static string DeadStream => _prefix + "functions:dead";

        /// <summary>
        /// Control-plane-side dead letters: a <c>functions:results</c> or
        /// <c>functions:build-results</c> entry that could not be applied after
        /// <see cref="Consumers.FunctionResultConsumer"/>'s delivery-count bound. Distinct from
        /// <see cref="DeadStream"/>, which is the runner's own dead letter for
        /// <c>functions:runs</c>/<c>functions:builds</c> — different consumers, different sides
        /// of the same contract, so a bad entry on one side is never mistaken for the other.
        /// </summary>
        public static string DeadResultsStream => _prefix + "functions:dead-results";

        // ---- consumer groups ----------------------------------------------------
        /// <summary>The group runners join. The control plane never reads these.</summary>
        public const string RunnerGroup = "runners";

        /// <summary>The group this Worker joins on the result streams.</summary>
        public const string LogicWorkerGroup = "logic-workers";

        // ---- per-entity keys ----------------------------------------------------
        public static string Run(string runId) => $"{_prefix}function:run:{runId}";
        public static string Result(string runId) => $"{_prefix}function:result:{runId}";
        public static string Logs(string runId) => $"{_prefix}function:logs:{runId}";
        public static string Lease(string runId) => $"{_prefix}function:lease:{runId}";
        public static string Cancel(string runId) => $"{_prefix}function:cancel:{runId}";

        /// <summary>
        /// Holds for <see cref="TestRateWindow"/> after a test is accepted for this function, so the
        /// next one is told to wait rather than queued. Per function, because that is what a
        /// developer hammers.
        /// </summary>
        public static string TestRate(string functionId) => $"{_prefix}function:test-rate:{functionId}";

        /// <summary>
        /// How long a function must wait between tests.
        /// <para>
        /// A test builds and runs on a host that is also serving deployed functions, so the cost of
        /// one is real. Two minutes is long enough that nobody can hold a host down by clicking, and
        /// short enough that it does not get in the way of working.
        /// </para>
        /// </summary>
        public static readonly TimeSpan TestRateWindow = TimeSpan.FromSeconds(120);
        public static string SyncChannel(string runId) => $"{_prefix}function:sync:{runId}";
        public static string Concurrency(string functionId) => $"{_prefix}function:concurrency:{functionId}";
        public static string Runner(string runnerId) => $"{_prefix}function:runner:{runnerId}";
        public static string Source(string buildId) => $"{_prefix}function:source:{buildId}";

        /// <summary>
        /// The run id of the test currently in flight for a function, so a new test can supersede
        /// it. Expires with <see cref="RunTtl"/>; a stale value is harmless, because cancelling a
        /// run that has already finished is ignored.
        /// </summary>
        public static string CurrentTest(string functionId) => $"{_prefix}function:test-current:{functionId}";
        public static string ImagesKeep => _prefix + "functions:images:keep";

        /// <summary>Rate-limit counters. Only ever touched when rate limiting is switched on.</summary>
        public static string RateMinute(string functionId, DateTime utc)
            => $"{_prefix}function:rate:{functionId}:{utc:yyyyMMddHHmm}";

        /// <summary>
        /// Hash of the anonymous poll token for one run (<c>tenant</c>, <c>hash</c>), expiring
        /// with <see cref="RunTtl"/>. The plaintext token is never stored.
        /// </summary>
        public static string PollToken(string runId) => $"{_prefix}function:poll:{runId}";

        /// <summary>
        /// Per function per UTC day — the limit it enforces is the function's own
        /// <c>RequestsPerDay</c>. The tenant stays in the key so a day's counters for one tenant
        /// can be found (and cleared) together.
        /// </summary>
        public static string QuotaDay(string tenantId, string functionId, DateTime utc)
            => $"{_prefix}function:quota:{tenantId}:{functionId}:{utc:yyyyMMdd}";

        /// <summary>Sorted set of runs awaiting a retry, scored by the epoch second they are due.</summary>
        public static string RetryQueue => _prefix + "functions:retries";

        /// <summary>
        /// Prefix of the marker written when a dead-lettered job has been applied to Mongo. The
        /// dead stream keeps its entries for forensics rather than deleting them on
        /// acknowledgement, so a reclaim or a second Worker can re-deliver one that was already
        /// applied; this makes the second pass a no-op instead of a duplicate audit record.
        /// </summary>
        public static string DeadApplied(string entryId) => $"{_prefix}function:dead-applied:{entryId}";

        // ---- time to live -------------------------------------------------------
        // This TTL is effectively "how long a Worker or runner may be down without losing work".
        // Redis holds the payload; Mongo holds the record. If the payload expires before a
        // consumer reaches it, the stream entry survives but there is nothing left to apply, so
        // the run is lost. Everything the payload is actually needed for finishes in minutes —
        // the sync wait caps at 180 s (Functions:SyncWaitMaxSeconds; 30 s over HTTP, Functions:HttpSyncWaitMaxSeconds), a retry's backoff at MaxDelaySeconds (300 s by default),
        // reclaim triggers at 90 s idle — so the remainder is purely outage headroom.
        // Mirrored in the runner's RedisKeys and in plan/PROTOCOL.md; change all three together.
        public static readonly TimeSpan RunTtl = TimeSpan.FromHours(6);

        /// <summary>
        /// Absolute lifetime of a run's delegation grant (<see cref="RunDelegationField"/>). Covers
        /// the longest retry chain the validators allow — 5 attempts of 90 s with 4 delays of up to
        /// 900 s, about 68 minutes — with room for queueing. It bounds how long the grant can be
        /// redeemed, not the token: each redemption yields a token of IAM's own short lifetime, and
        /// IAM re-checks the user every time. A run still queued past it starts without a token
        /// rather than failing.
        /// </summary>
        public static readonly TimeSpan DelegationGrantTtl = TimeSpan.FromHours(2);
        public static readonly TimeSpan SourceTtl = TimeSpan.FromHours(1);
        public static readonly TimeSpan CancelTtl = TimeSpan.FromSeconds(120);

        /// <summary>
        /// How long a <see cref="DeadApplied"/> marker lives. Comfortably longer than the dead
        /// stream's own retention window is not needed — only longer than the window in which an
        /// unacknowledged entry can still be reclaimed, which is minutes.
        /// </summary>
        public static readonly TimeSpan DeadAppliedTtl = TimeSpan.FromHours(24);

        // ---- wire status strings ------------------------------------------------
        /// <summary>
        /// The runner sends these exact strings. Kept as constants rather than parsed by
        /// enum name so a rename of <see cref="Enums.RunStatus"/> cannot silently change the
        /// wire contract.
        /// </summary>
        public static class Wire
        {
            public const string Queued = "QUEUED";
            public const string Claimed = "CLAIMED";
            public const string Starting = "STARTING";
            public const string Running = "RUNNING";
            public const string OutputProcessing = "OUTPUT_PROCESSING";
            public const string Succeeded = "SUCCEEDED";
            public const string Failed = "FAILED";
            public const string TimedOut = "TIMED_OUT";
            public const string Cancelled = "CANCELLED";
            public const string ResourceExceeded = "RESOURCE_EXCEEDED";
            public const string OutputFailed = "OUTPUT_FAILED";

            public const string MemoryLimit = "MEMORY_LIMIT";
            public const string PidLimit = "PID_LIMIT";
            public const string UserRuntimeError = "USER_RUNTIME_ERROR";
            public const string RuntimeStartFailed = "RUNTIME_START_FAILED";
            public const string ResultTooLarge = "RESULT_TOO_LARGE";
            public const string ResultNotSerializable = "RESULT_NOT_SERIALIZABLE";
            public const string ImagePullFailed = "IMAGE_PULL_FAILED";
            public const string TimedOutCode = "TIMED_OUT";
            public const string SandboxStartFailed = "SANDBOX_START_FAILED";
            public const string BuildFailedCode = "BUILD_FAILED";
            public const string SecretUnresolved = "SECRET_UNRESOLVED";
            public const string SecretStoreUnavailable = "SECRET_STORE_UNAVAILABLE";

            /// <summary>
            /// Not sent by the runner — determined here, by
            /// <see cref="Consumers.FunctionDeadLetterConsumer"/>, for a job the runner moved to
            /// <see cref="DeadStream"/> without ever executing it.
            /// </summary>
            public const string Undeliverable = "UNDELIVERABLE";

            public const string BuildSucceeded = "SUCCEEDED";
            public const string BuildFailed = "FAILED";
        }
    }
}
