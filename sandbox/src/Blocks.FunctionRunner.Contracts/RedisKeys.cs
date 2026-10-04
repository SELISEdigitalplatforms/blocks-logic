namespace Blocks.FunctionRunner.Contracts
{
    /// <summary>
    /// Every Redis key and stream name in the contract, in one place.
    /// <para>
    /// This file is <c>plan/PROTOCOL.md</c> as code. The control plane (blocks-logic) carries
    /// its own copy of the same names; if one side changes a name here without changing the
    /// other, runs stop being delivered rather than failing loudly, so treat any edit as a
    /// protocol change and version it.
    /// </para>
    /// </summary>
    public static class RedisKeys
    {
        /// <summary>Protocol version carried on every message. Bump only with both halves.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>
        /// Namespace for every key and stream below, so two environments can share one Redis
        /// without sharing work. Mirrors <c>FunctionQueueKeys.Prefix</c> in blocks-logic.
        /// <para>
        /// Empty by default, which produces exactly the names this protocol has always used.
        /// Set it from <c>RUNNER__QueuePrefix</c>, and set <c>Functions:QueuePrefix</c> to the
        /// same value on the control plane that this runner serves. The two must agree: a
        /// mismatch does not fail loudly, the runner simply never sees any work.
        /// </para>
        /// <para>
        /// Without it the streams are fixed literals, so every runner pointed at a Redis joins
        /// the same consumer group and competes for the same entries — one developer's test gets
        /// built on another's host, against that host's private registry and its own build of
        /// this runner.
        /// </para>
        /// </summary>
        public static string Prefix
        {
            get => _prefix;
            set => _prefix = Normalize(value);
        }

        private static string _prefix = string.Empty;

        /// <summary>Blank stays blank; anything else gets exactly one trailing colon.</summary>
        public static string Normalize(string? prefix)
        {
            var trimmed = prefix?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return string.Empty;

            return trimmed.TrimEnd(':') + ":";
        }

        /// <summary>
        /// Protocol version of a <see cref="RunsStream"/> entry — the one message whose version
        /// has moved on. Version 2 means the envelope's <c>env</c> carries secret-bound variables
        /// as <c>{{secret.&lt;id&gt;}}</c> references, and the runner resolves them itself right
        /// before the sandbox starts (<c>Runs/EnvSecretReferences</c>); version 1 meant the
        /// control plane had already put the plaintext there.
        /// <para>
        /// The bump is the deploy guard. A runner that predates it dead-letters anything but 1,
        /// so a new control plane in front of an old runner fails its runs as
        /// <c>UNDELIVERABLE</c> — nothing executes — instead of handing a function the literal
        /// reference text as though it were a key. The other messages (results, builds) are
        /// unchanged and stay on <see cref="ProtocolVersion"/>.
        /// </para>
        /// </summary>
        public const int RunProtocolVersion = 2;

        /// <summary>
        /// The oldest run entry this runner still executes. Version 1 entries — plaintext already
        /// in <c>env</c>, from a control plane not yet upgraded — run exactly as they always did,
        /// with no resolution, so the runner can be deployed first. Raise to
        /// <see cref="RunProtocolVersion"/> once every control plane sends 2.
        /// </summary>
        public const int MinRunProtocolVersion = 1;

        // ---- streams -----------------------------------------------------------------
        /// <summary>Run jobs, written by the control plane, consumed by runners.</summary>
        public static string RunsStream => _prefix + "functions:runs";

        /// <summary>Run results, written by runners, consumed by the logic Worker.</summary>
        public static string ResultsStream => _prefix + "functions:results";

        /// <summary>Build jobs, written by the control plane, consumed by runners.</summary>
        public static string BuildsStream => _prefix + "functions:builds";

        /// <summary>Build results, written by runners, consumed by the logic Worker.</summary>
        public static string BuildResultsStream => _prefix + "functions:build-results";

        /// <summary>
        /// Test runs: each entry is one job that builds the function's current source into a
        /// local image, runs it once on the same host, and deletes the image. Separate from
        /// <see cref="RunsStream"/> so that a runner which predates it never sees one — an old
        /// runner would try to pull an image that exists on no registry.
        /// </summary>
        public static string TestsStream => _prefix + "functions:tests";

        /// <summary>Entries that exhausted their retry budget.</summary>
        public static string DeadStream => _prefix + "functions:dead";

        // ---- consumer groups ---------------------------------------------------------
        /// <summary>The group runners join on <see cref="RunsStream"/> and <see cref="BuildsStream"/>.</summary>
        public const string RunnerGroup = "runners";

        /// <summary>The group the logic Worker joins on the result streams.</summary>
        public const string LogicWorkerGroup = "logic-workers";

        // ---- per-entity keys ---------------------------------------------------------
        /// <summary>Hash holding the execution envelope and limits for a run. TTL 24 h.</summary>
        public static string Run(string runId) => $"{_prefix}function:run:{runId}";

        /// <summary>
        /// Optional field of the <see cref="Run"/> hash: the Genesis delegation grant id
        /// (<c>dg_</c> + 64 hex) of the caller who invoked the run. The runner redeems it with IAM
        /// right before the sandbox starts and hands the function the resulting token as
        /// <c>ctx.blocks.accessToken</c>. Beside the envelope, never inside it, so the envelope
        /// screen still holds; absent means the function gets no token. Needs no protocol bump: a
        /// runner that predates it ignores the field. Mirrors
        /// <c>FunctionQueueKeys.RunDelegationField</c> in the control plane.
        /// </summary>
        public const string RunDelegationField = "delegation";

        /// <summary>The serialized result of a run. TTL 24 h.</summary>
        public static string Result(string runId) => $"{_prefix}function:result:{runId}";

        /// <summary>NDJSON log lines for a run, as a list. TTL 24 h.</summary>
        public static string Logs(string runId) => $"{_prefix}function:logs:{runId}";

        /// <summary>The execution lease, taken with SET NX PX and renewed at a third of its TTL.</summary>
        public static string Lease(string runId) => $"{_prefix}function:lease:{runId}";

        /// <summary>Set by the control plane to ask the runner to kill a sandbox. TTL 120 s.</summary>
        public static string Cancel(string runId) => $"{_prefix}function:cancel:{runId}";

        /// <summary>Channel that wakes an API request waiting synchronously on a run.</summary>
        public static string SyncChannel(string runId) => $"{_prefix}function:sync:{runId}";

        /// <summary>Per-function concurrency counter; overflow queues and never rejects.</summary>
        public static string Concurrency(string functionId) => $"{_prefix}function:concurrency:{functionId}";

        /// <summary>
        /// A test run's concurrency slot, kept apart from <see cref="Concurrency"/> so testing a
        /// function can never consume the budget its deployed version serves traffic with.
        /// </summary>
        public static string TestConcurrency(string functionId) => $"{_prefix}function:test-slot:{functionId}";

        /// <summary>
        /// Sandbox slots held by one tenant across the fleet. The host budget alone is
        /// first-come-first-served, so one busy tenant could hold every slot on a runner and
        /// every other tenant waited behind it; this is the cap that makes the share fair.
        /// </summary>
        public static string TenantSlots(string tenantId) => $"{_prefix}function:tenant-slots:{tenantId}";

        /// <summary>Runner heartbeat hash. TTL 15 s, so a dead runner disappears quickly.</summary>
        public static string Runner(string runnerId) => $"{_prefix}function:runner:{runnerId}";

        /// <summary>Source bundle for a build. TTL 1 h.</summary>
        public static string Source(string buildId) => $"{_prefix}function:source:{buildId}";

        /// <summary>Set of image references that image GC must not prune.</summary>
        public static string ImagesKeep => _prefix + "functions:images:keep";

        // ---- time to live ------------------------------------------------------------
        // These three are effectively "how long a Worker or runner may be down without losing
        // work": Redis holds the payload, Mongo holds the record, and if a payload expires before
        // its consumer reaches it the stream entry survives with nothing left to apply. Everything
        // they are genuinely needed for finishes in minutes (sync wait ≤ 180 s — the control
        // plane's Functions:SyncWaitMaxSeconds, retry backoff ≤ 300 s, reclaim at 90 s idle);
        // the rest is outage headroom. This once said 60 s against the control plane's 180 s,
        // which made the headroom look three times larger than it is.
        // Mirrored in blocks-logic's FunctionQueueKeys and in plan/PROTOCOL.md — change all three.
        public static readonly TimeSpan RunTtl = TimeSpan.FromHours(6);
        public static readonly TimeSpan ResultTtl = TimeSpan.FromHours(6);
        public static readonly TimeSpan LogsTtl = TimeSpan.FromHours(6);
        public static readonly TimeSpan SourceTtl = TimeSpan.FromHours(1);
        public static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan CancelTtl = TimeSpan.FromSeconds(120);
    }
}
