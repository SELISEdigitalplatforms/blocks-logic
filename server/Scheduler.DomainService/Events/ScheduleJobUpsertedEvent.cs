namespace Scheduler.DomainService.Events
{
    public class ScheduleJobUpsertedEvent
    {
        public string ItemId { get; set; }
        public string TenantId { get; set; }
    }
}
