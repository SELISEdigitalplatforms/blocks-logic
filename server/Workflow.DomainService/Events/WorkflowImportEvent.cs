namespace Workflow.DomainService.Events
{
    public class WorkflowImportEvent
    {
        public string FileId { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
    }
}
