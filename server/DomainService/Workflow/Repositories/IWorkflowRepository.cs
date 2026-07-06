using DomainService.Workflow.Models;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowRepository
    {
        Task CreateWorkflowAsync(WorkflowModel workflow);
        Task<List<WorkflowModel>> GetAllWorkflowsAsync(int pageSize, int pageNumber, string? search, bool? isActive, string tenantId);

        Task<List<WorkflowModel>> GetWorkflowsByMailServerConfigurationIdAsync(string mailServerConfigurationId, string tenantId);
        Task<List<WorkflowModel>> GetWorkflowsByDataCollectionAsync(string collectionName, string operation, string tenantId);
        Task<List<WorkflowModel>> GetPublishWorkflowsByDataCollectionAsync(string collectionName, string operation, string tenantId);
        Task<long> GetWorkflowsCountAsync(string? search, bool? isActive, string tenantId);
        Task<WorkflowModel> GetWorkflowAsync(string workflowId, string tenantId);
        Task UpdateWorkflowAsync(WorkflowModel workflow);
        Task DeleteWorkflowAsync(string workflowId, string tenantId);
    }
}