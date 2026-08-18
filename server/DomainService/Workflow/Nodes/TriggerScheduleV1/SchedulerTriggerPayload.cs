namespace DomainService.Workflow.Nodes.TriggerScheduleV1
{
    /// <summary>
    /// Payload published by the Scheduler when a workflow schedule trigger fires.
    /// The scheduler's <see cref="Scheduler.DomainService.Models.PublishScheduleCommand"/> carries this
    /// JSON in its Payload field; the SchedulerTriggerConsumer deserializes it into this type.
    /// </summary>
    public class SchedulerTriggerPayload
    {
        public string WorkflowId { get; set; } = string.Empty;
        public string TriggerId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public DateTime? FiredAt { get; set; }
    }
}
