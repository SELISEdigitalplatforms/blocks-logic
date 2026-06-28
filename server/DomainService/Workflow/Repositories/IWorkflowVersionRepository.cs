using DomainService.Workflow.Models;

namespace DomainService.Workflow.Repositories
{


    public interface IWorkflowVersionRepository
    {
        Task CreateWorkflowVersionAsync(WorkflowVersionModel versionModel);
        Task<List<WorkflowVersionModel>> GetWorkflowVersionsAsync(string projectKey, string workflowId, WorkflowVersionFilter? query = null);
        Task<WorkflowVersionModel> GetWorkflowVersionAsync(string projectKey, string versionId);
        Task<WorkflowVersionModel> UpdateWorkflowVersionAsync(string projectKey, string versionId, WorkflowVersionModel versionModel);
        Task<WorkflowVersionModel> GetPublishedWorkflowVersionAsync(string projectKey, string workflowId);

    }
}