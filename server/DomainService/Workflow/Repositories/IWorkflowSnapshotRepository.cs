using DomainService.Workflow.Models;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowSnapshotRepository
    {
        Task CreateWorkflowSnapshotAsync(WorkflowSnapshotModel snapshotModel);
        Task<List<WorkflowSnapshotModel>> GetWorkflowSnapshotsAsync(string projectKey, string workflowId);
        Task<WorkflowSnapshotModel> GetWorkflowSnapshotAsync(string projectKey, string id);

    }
}