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
        /// Function events (logic-owned, not Genesis entities): the events a function may send, per function
        /// (draft, <c>VersionId</c> null) and per deployed version.
        /// </summary>
        public const string FunctionEventSettingsCollection = "FunctionEventSettings";

        /// <summary>What happened to the events of each run attempt (sent, failed, rejected). Never the payload.</summary>
        public const string FunctionRunEventsCollection = "FunctionRunEvents";

        /// <summary>
        /// Not in the original plan's Mongo table (which lists Functions, FunctionVersions,
        /// FunctionRuns, FunctionRunLogs, FunctionAuditEvents only) — added because the build
        /// cache (DECISIONS D3: one image serves both Test and Deploy) needs somewhere durable
        /// to record a build's outcome that survives past the source blob's 1 h Redis TTL.
        /// </summary>
        public const string FunctionBuildsCollection = "FunctionBuilds";

        /// <summary>Runs older than this are eligible for TTL deletion.</summary>
        public static readonly TimeSpan RunRetention = TimeSpan.FromDays(30);

        /// <summary>Run event results older than this are eligible for TTL deletion (same as the run record).</summary>
        public static readonly TimeSpan RunEventsRetention = TimeSpan.FromDays(30);

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
            /// <summary>
            /// No longer written — rolling back was removed. Kept so audit rows recorded while it
            /// existed still render with a label rather than a raw action string.
            /// </summary>
            public const string RolledBack = "FunctionRolledBack";
            public const string Paused = "FunctionPaused";
            public const string Resumed = "FunctionResumed";
            public const string RunCancelled = "RunCancelled";
            public const string RunReplayed = "RunReplayed";
            /// <summary>An export file was downloaded. Detail: the content hash only.</summary>
            public const string Exported = "FunctionExported";
            /// <summary>An import created or changed the function. Detail: status, changed fields, content hash — never source or values.</summary>
            public const string Imported = "FunctionImported";
            /// <summary>The events a function may send (or is started by) were saved. Detail: the types and the count of trigger names.</summary>
            public const string EventsUpdated = "FunctionEventsUpdated";
        }
    }
}
