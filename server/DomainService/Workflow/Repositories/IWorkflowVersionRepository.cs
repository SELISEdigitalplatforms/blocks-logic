using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowVersionRepository
    {
        Task CreateWorkflowVersionAsync(WorkflowVersionEntity versionModel);
        Task<List<WorkflowVersionEntity>> GetWorkflowVersionsAsync(string projectKey, string[] workflowIds);
        Task<WorkflowVersionEntity> GetWorkflowVersionAsync(string projectKey, string versionId);
        Task<WorkflowVersionEntity> UpdateWorkflowVersionAsync(string projectKey, string versionId, WorkflowVersionEntity versionModel);
        Task DeleteWorkflowVersionAsync(string projectKey, string versionId);
        Task DeleteWorkflowVersionsByWorkflowIdAsync(string projectKey, string workflowId);

    }
}