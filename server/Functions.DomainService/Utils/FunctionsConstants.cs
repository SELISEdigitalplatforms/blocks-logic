namespace Functions.DomainService.Utils
{
    /// <summary>Collection names and other constants shared across the Functions module.</summary>
    public static class FunctionsConstants
    {
        public const string FunctionsCollection = "Functions";
        public const string FunctionVersionsCollection = "FunctionVersions";
        public const string FunctionRunsCollection = "FunctionRuns";

        /// <summary>
        /// Retired. Run counters used to live here, one document per function, because Save and
        /// Update once replaced the whole function document and would have overwritten them. Every
        /// write to it is field-level now, so the counters are fields of the function
        /// (<c>TotalRuns</c>, <c>LastRunAt</c>). Only <c>FunctionRunStatsMigration</c> reads this
        /// name, to carry old counts over and drop the collection.
        /// </summary>
        public const string LegacyRunStatsCollection = "FunctionRunStats";
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
            /// <summary>The background purge after a delete has finished; carries what it removed.</summary>
            public const string Purged = "FunctionPurged";
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
