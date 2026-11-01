namespace Scheduler.DomainService.Dtos.Responses
{
    /// <summary>A light history row: no body, no snapshot.</summary>
    public class ScheduleExecutionListItemDto
    {
        public required string Id { get; set; }
        public DateTime? FiredUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }

        /// <summary><c>FinishedUtc - FiredUtc</c> in ms; null when either is missing.</summary>
        public long? DurationMs { get; set; }

        /// <summary>The stored HTTP status; null when absent or not an integer.</summary>
        public int? Status { get; set; }

        /// <summary><c>Succeeded</c> (status 200-299) or <c>Failed</c>. Derived, never stored.</summary>
        public required string Outcome { get; set; }
    }
}
