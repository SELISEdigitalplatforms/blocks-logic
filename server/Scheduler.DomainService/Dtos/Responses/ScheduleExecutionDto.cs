namespace Scheduler.DomainService.Dtos.Responses
{
    /// <summary>A full history row, for the details sheet.</summary>
    public class ScheduleExecutionDto
    {
        public required string Id { get; set; }
        public required string ScheduleId { get; set; }
        public string? ScheduleName { get; set; }
        public DateTime? FiredUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
        public long? DurationMs { get; set; }
        public required string Outcome { get; set; }

        /// <summary><c>Response["Status"]</c>; null when absent or not an integer.</summary>
        public int? Status { get; set; }

        /// <summary><c>Response["Body"]</c>; a non-string value is returned as its text.</summary>
        public string? Body { get; set; }

        /// <summary>The schedule at fire time, as stored (camelCase JSON, secrets redacted).</summary>
        public string? Snapshot { get; set; }
    }
}
