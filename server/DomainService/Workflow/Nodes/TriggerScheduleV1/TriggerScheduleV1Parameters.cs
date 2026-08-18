namespace DomainService.Workflow.Nodes.TriggerScheduleV1
{
    /// <summary>
    /// Parameters for the Schedule Trigger node.
    /// Stored in the workflow node's Parameters BsonDocument.
    /// </summary>
    public class TriggerScheduleV1Parameters
    {
        /// <summary>
        /// The trigger interval mode: minutes, hours, days, weeks, months, or custom
        /// </summary>
        public string TriggerInterval { get; set; } = "days";

        /// <summary>
        /// The generated or user-provided cron expression (5- or 6-field)
        /// </summary>
        public string CronExpression { get; set; } = string.Empty;

        public int? SecondsBetweenTriggers { get; set; }
        public int? MinutesBetweenTriggers { get; set; }
        public int? HoursBetweenTriggers { get; set; }
        public int? MonthsBetweenTriggers { get; set; }
        public string? TriggerAtHour { get; set; }
        public int? TriggerAtMinute { get; set; }
        public int? TriggerAtDayOfMonth { get; set; }
        public List<string>? TriggerAtWeekdays { get; set; }
    }
}
