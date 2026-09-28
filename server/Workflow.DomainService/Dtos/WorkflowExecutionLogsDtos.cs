using Blocks.Genesis;

namespace Workflow.DomainService.Dtos
{
    public class WorkflowExecutionLogsGetRequestDto
    {
        public required string ExecutionId { get; set; }
    }

    public class WorkflowExecutionLogsGetResponseDto : BaseResponse
    {
        public WorkflowExecutionLogsDto? Data { get; set; }
    }

    public class WorkflowExecutionLogsDto
    {
        public required string ExecutionId { get; set; }
        public string? TraceId { get; set; }
        public required ExecutionLogsAvailability Availability { get; set; }

        /// <summary>StartedAt + retention; null when TraceId is null.</summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>Effective retention used for this response: LMT's HotDataRetentionPeriodInDays, or the config fallback.</summary>
        public int RetentionDays { get; set; }

        /// <summary>The client keeps polling while true: the run is still going or finished only moments ago.</summary>
        public bool MayStillArrive { get; set; }

        /// <summary>More lines exist than MaxLines; only the first MaxLines are returned.</summary>
        public bool IsTruncated { get; set; }

        public List<ExecutionLogEntryDto> Logs { get; set; } = new();
    }

    public enum ExecutionLogsAvailability
    {
        /// <summary>Query ran (Logs may still be empty).</summary>
        Available,
        /// <summary>StartedAt older than retention; query not run (logs moved to cold storage).</summary>
        Expired,
        /// <summary>TraceId null (pre-feature execution); query not run.</summary>
        NotRecorded,
        /// <summary>Store not configured or the store call failed.</summary>
        SourceUnavailable,
    }

    public class ExecutionLogEntryDto
    {
        /// <summary>UTC.</summary>
        public required DateTime Timestamp { get; set; }

        /// <summary>"Information" | "Warning" | "Error" (Serilog names).</summary>
        public required string Level { get; set; }

        /// <summary>e.g. "node.started".</summary>
        public required string Stage { get; set; }

        public string? NodeId { get; set; }
        public int? RunIndex { get; set; }

        /// <summary>Resolved server-side from the execution's workflow snapshot.</summary>
        public string? NodeName { get; set; }
        public string? NodeType { get; set; }

        /// <summary>Text after the [wf:…] / [node:…] prefix.</summary>
        public required string Message { get; set; }

        /// <summary>"api" | "worker" (from ServiceName).</summary>
        public required string Source { get; set; }
    }
}
