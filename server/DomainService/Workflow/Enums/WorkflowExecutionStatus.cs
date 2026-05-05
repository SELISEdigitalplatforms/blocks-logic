namespace DomainService.Workflow.Enums
{
    public enum WorkflowExecutionStatus
    {
        Init = 0,
        Queued = 1,
        Pending = 2,
        Running = 3,
        Completed = 4,
        Failed = 5,
    }
}