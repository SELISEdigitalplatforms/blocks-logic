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

        // ---- streams ------------------------------------------------------------
        public const string RunsStream = "functions:runs";
        public const string ResultsStream = "functions:results";
        public const string BuildsStream = "functions:builds";
        public const string BuildResultsStream = "functions:build-results";
        public const string DeadStream = "functions:dead";

        /// <summary>
        /// Control-plane-side dead letters: a <c>functions:results</c> or
        /// <c>functions:build-results</c> entry that could not be applied after
        /// <see cref="Consumers.FunctionResultConsumer"/>'s delivery-count bound. Distinct from
        /// <see cref="DeadStream"/>, which is the runner's own dead letter for
        /// <c>functions:runs</c>/<c>functions:builds</c> — different consumers, different sides
        /// of the same contract, so a bad entry on one side is never mistaken for the other.
        /// </summary>
        public const string DeadResultsStream = "functions:dead-results";

        // ---- consumer groups ----------------------------------------------------
        /// <summary>The group runners join. The control plane never reads these.</summary>
        public const string RunnerGroup = "runners";

        /// <summary>The group this Worker joins on the result streams.</summary>
        public const string LogicWorkerGroup = "logic-workers";

        // ---- per-entity keys ----------------------------------------------------
        public static string Run(string runId) => $"function:run:{runId}";
        public static string Result(string runId) => $"function:result:{runId}";
        public static string Logs(string runId) => $"function:logs:{runId}";
        public static string Lease(string runId) => $"function:lease:{runId}";
        public static string Cancel(string runId) => $"function:cancel:{runId}";
        public static string SyncChannel(string runId) => $"function:sync:{runId}";
        public static string Concurrency(string functionId) => $"function:concurrency:{functionId}";
        public static string Runner(string runnerId) => $"function:runner:{runnerId}";
        public static string Source(string buildId) => $"function:source:{buildId}";
        public const string ImagesKeep = "functions:images:keep";

        /// <summary>Rate-limit counters. Only ever touched when rate limiting is switched on.</summary>
        public static string RateMinute(string functionId, DateTime utc)
            => $"function:rate:{functionId}:{utc:yyyyMMddHHmm}";

        public static string QuotaDay(string tenantId, DateTime utc)
            => $"function:quota:{tenantId}:{utc:yyyyMMdd}";

        /// <summary>Sorted set of runs awaiting a retry, scored by the epoch second they are due.</summary>
        public const string RetryQueue = "functions:retries";

        // ---- time to live -------------------------------------------------------
        // This TTL is effectively "how long a Worker or runner may be down without losing work".
        // Redis holds the payload; Mongo holds the record. If the payload expires before a
        // consumer reaches it, the stream entry survives but there is nothing left to apply, so
        // the run is lost. Everything the payload is actually needed for finishes in minutes —
        // the sync wait caps at 60 s, a retry's backoff at MaxDelaySeconds (300 s by default),
        // reclaim triggers at 90 s idle — so the remainder is purely outage headroom.
        // Mirrored in the runner's RedisKeys and in plan/PROTOCOL.md; change all three together.
        public static readonly TimeSpan RunTtl = TimeSpan.FromHours(6);
        public static readonly TimeSpan SourceTtl = TimeSpan.FromHours(1);
        public static readonly TimeSpan CancelTtl = TimeSpan.FromSeconds(120);

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

            public const string BuildSucceeded = "SUCCEEDED";
            public const string BuildFailed = "FAILED";
        }
    }
}
