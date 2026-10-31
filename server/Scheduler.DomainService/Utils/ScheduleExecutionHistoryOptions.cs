namespace Scheduler.DomainService.Utils
{
    /// <summary>Configuration section <c>Scheduler:ExecutionHistory</c>.</summary>
    public sealed class ScheduleExecutionHistoryOptions
    {
        public const string SectionName = "Scheduler:ExecutionHistory";

        /// <summary>Kill switch for the per-fire history insert. Delivery is unchanged either way.</summary>
        public bool Enabled { get; set; } = true;
    }
}
