using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowVersionRepository
    {
        Task CreateWorkflowVersionAsync(WorkflowVersionEntity versionModel);
        Task<List<WorkflowVersionEntity>> GetWorkflowVersionsAsync(string tenantId, string workflowId);
        Task<List<WorkflowVersionEntity>> GetWorkflowVersionsAsync(string tenantId, string[] workflowIds);
        Task<WorkflowVersionEntity> GetWorkflowVersionAsync(string tenantId, string versionId);
        Task<WorkflowVersionEntity> UpdateWorkflowVersionAsync(string tenantId, string versionId, WorkflowVersionEntity versionModel);
        Task DeleteWorkflowVersionAsync(string tenantId, string versionId);
        Task DeleteWorkflowVersionsByWorkflowIdAsync(string tenantId, string workflowId);

    }
}