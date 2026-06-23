using DomainService.Workflow.Models;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowVersionRepository
    {
        Task CreateWorkflowVersionAsync(WorkflowVersionModel versionModel);
        Task<List<WorkflowVersionModel>> GetWorkflowVersionsAsync(string projectKey, string workflowId);
        Task<WorkflowVersionModel> GetWorkflowVersionAsync(string projectKey, string id);

    }
}