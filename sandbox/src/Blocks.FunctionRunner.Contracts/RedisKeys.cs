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

        // ---- streams -----------------------------------------------------------------
        /// <summary>Run jobs, written by the control plane, consumed by runners.</summary>
        public const string RunsStream = "functions:runs";

        /// <summary>Run results, written by runners, consumed by the logic Worker.</summary>
        public const string ResultsStream = "functions:results";

        /// <summary>Build jobs, written by the control plane, consumed by runners.</summary>
        public const string BuildsStream = "functions:builds";

        /// <summary>Build results, written by runners, consumed by the logic Worker.</summary>
        public const string BuildResultsStream = "functions:build-results";

        /// <summary>Entries that exhausted their retry budget.</summary>
        public const string DeadStream = "functions:dead";

        // ---- consumer groups ---------------------------------------------------------
        /// <summary>The group runners join on <see cref="RunsStream"/> and <see cref="BuildsStream"/>.</summary>
        public const string RunnerGroup = "runners";

        /// <summary>The group the logic Worker joins on the result streams.</summary>
        public const string LogicWorkerGroup = "logic-workers";

        // ---- per-entity keys ---------------------------------------------------------
        /// <summary>Hash holding the execution envelope and limits for a run. TTL 24 h.</summary>
        public static string Run(string runId) => $"function:run:{runId}";

        /// <summary>The serialized result of a run. TTL 24 h.</summary>
        public static string Result(string runId) => $"function:result:{runId}";

        /// <summary>NDJSON log lines for a run, as a list. TTL 24 h.</summary>
        public static string Logs(string runId) => $"function:logs:{runId}";

        /// <summary>The execution lease, taken with SET NX PX and renewed at a third of its TTL.</summary>
        public static string Lease(string runId) => $"function:lease:{runId}";

        /// <summary>Set by the control plane to ask the runner to kill a sandbox. TTL 120 s.</summary>
        public static string Cancel(string runId) => $"function:cancel:{runId}";

        /// <summary>Channel that wakes an API request waiting synchronously on a run.</summary>
        public static string SyncChannel(string runId) => $"function:sync:{runId}";

        /// <summary>Per-function concurrency counter; overflow queues and never rejects.</summary>
        public static string Concurrency(string functionId) => $"function:concurrency:{functionId}";

        /// <summary>Runner heartbeat hash. TTL 15 s, so a dead runner disappears quickly.</summary>
        public static string Runner(string runnerId) => $"function:runner:{runnerId}";

        /// <summary>Source bundle for a build. TTL 1 h.</summary>
        public static string Source(string buildId) => $"function:source:{buildId}";

        /// <summary>Set of image references that image GC must not prune.</summary>
        public const string ImagesKeep = "functions:images:keep";

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
