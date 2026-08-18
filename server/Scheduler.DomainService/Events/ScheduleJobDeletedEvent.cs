namespace Scheduler.DomainService.Events
{
    public class ScheduleJobDeletedEvent
    {
        public string ItemId { get; set; }
        public string TenantId { get; set; }
    }
}
