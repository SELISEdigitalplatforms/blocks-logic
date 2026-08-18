namespace Scheduler.DomainService.Dtos.Requests
{
    public class CreateWorkflowScheduleRequest
    {
        public string WorkflowId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
