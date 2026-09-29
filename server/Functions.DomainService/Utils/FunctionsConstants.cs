namespace Functions.DomainService.Utils
{
    /// <summary>Collection names and other constants shared across the Functions module.</summary>
    public static class FunctionsConstants
    {
        public const string FunctionsCollection = "Functions";
        public const string FunctionVersionsCollection = "FunctionVersions";
        public const string FunctionRunsCollection = "FunctionRuns";

        /// <summary>
        /// Per-function run counters, one small document per function. Deliberately not fields on
        /// the function itself: Save and Update replace that whole document from a copy loaded
        /// earlier, so a counter living there is silently overwritten by any run that happened in
        /// between — and run traffic would be rewriting the configuration document on every
        /// invocation for no reason.
        /// </summary>
        public const string FunctionRunStatsCollection = "FunctionRunStats";
        public const string FunctionRunLogsCollection = "FunctionRunLogs";
        public const string FunctionAuditEventsCollection = "FunctionAuditEvents";

        /// <summary>
        /// Not in the original plan's Mongo table (which lists Functions, FunctionVersions,
        /// FunctionRuns, FunctionRunLogs, FunctionAuditEvents only) — added because the build
        /// cache (DECISIONS D3: one image serves both Test and Deploy) needs somewhere durable
        /// to record a build's outcome that survives past the source blob's 1 h Redis TTL.
        /// </summary>
        public const string FunctionBuildsCollection = "FunctionBuilds";

        /// <summary>Runs older than this are eligible for TTL deletion.</summary>
        public static readonly TimeSpan RunRetention = TimeSpan.FromDays(30);

        /// <summary>Run logs older than this are eligible for TTL deletion.</summary>
        public static readonly TimeSpan RunLogRetention = TimeSpan.FromDays(30);

        /// <summary>Audit events older than this are eligible for TTL deletion.</summary>
        public static readonly TimeSpan AuditRetention = TimeSpan.FromDays(365);

        /// <summary>Builds older than this are eligible for TTL deletion, successful or not.</summary>
        public static readonly TimeSpan BuildRetention = TimeSpan.FromDays(14);

        // ---- audit action names --------------------------------------------------------
        public static class AuditActions
        {
            public const string Created = "FunctionCreated";
            public const string Updated = "FunctionUpdated";
            public const string Deleted = "FunctionDeleted";
            public const string Saved = "FunctionSaved";
            public const string Tested = "FunctionTested";
            public const string Deployed = "FunctionDeployed";
            public const string VersionActivated = "VersionActivated";
            public const string RolledBack = "FunctionRolledBack";
            public const string Paused = "FunctionPaused";
            public const string Resumed = "FunctionResumed";
            public const string RunCancelled = "RunCancelled";
            public const string RunReplayed = "RunReplayed";
        }
    }
}
