namespace Scheduler.DomainService.Dtos.Requests
{
    /// <summary>The next few fires of a cron in a time zone, for the schedule form. Validated by the service.</summary>
    public class PreviewScheduleRunsRequestDto
    {
        public string CronExpression { get; set; } = string.Empty;

        /// <summary>IANA id; null, "UTC" or "Etc/UTC" = UTC.</summary>
        public string? TimeZone { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        /// <summary>How many fires to return, 1-5.</summary>
        public int Count { get; set; } = 3;
    }
}
