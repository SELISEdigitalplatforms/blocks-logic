using DomainService.Workflow.Models;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowRepository
    {
        Task CreateWorkflowAsync(WorkflowModel workflow);

        Task<List<WorkflowModel>> GetAllWorkflowsAsync(string tenantId, int pageSize, int pageNumber, string? search, bool? isPublished);

        Task<List<WorkflowModel>> GetWorkflowsByMailServerConfigurationIdAsync(string tenantId, string mailServerConfigurationId);

        Task<List<WorkflowModel>> GetWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);

        Task<List<WorkflowModel>> GetPublishWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);

        Task<long> GetWorkflowsCountAsync(string tenantId, string? search, bool? isPublished);

        Task<WorkflowModel> GetWorkflowAsync(string tenantId, string workflowId);

        Task UpdateWorkflowAsync(WorkflowModel workflow);

        Task DeleteWorkflowAsync(string tenantId, string workflowId);
    }
}