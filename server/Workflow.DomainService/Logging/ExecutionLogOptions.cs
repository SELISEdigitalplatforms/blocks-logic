namespace Workflow.DomainService.Logging
{
    /// <summary>Configuration section <c>Workflow:ExecutionLogs</c>.</summary>
    public sealed class ExecutionLogOptions
    {
        public const string SectionName = "Workflow:ExecutionLogs";

        /// <summary>Fallback only, used when LMT's HotDataRetentionPeriodInDays can't be read.</summary>
        public int RetentionDays { get; set; } = 30;

        /// <summary>Query window padding and the MayStillArrive window.</summary>
        public int IngestionGraceSeconds { get; set; } = 120;

        /// <summary>Maximum lines returned per execution.</summary>
        public int MaxLines { get; set; } = 2000;

        /// <summary>Log collections to read, equal to the ServiceName values blocks-logic logs under. An array
        /// (not a list) so configuration replaces the default instead of appending to it.</summary>
        public string[] Collections { get; set; } = ["blocks-logic", "blocks-logic-worker"];
    }
}
