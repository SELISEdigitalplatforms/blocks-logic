using DomainService.Workflow.Entities;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowRepository
    {
        Task CreateWorkflowAsync(WorkflowEntity workflow);

        Task<List<WorkflowEntity>> GetAllWorkflowsAsync(string tenantId, int pageSize, int pageNumber, string? search, bool? isPublished);

        Task<List<WorkflowEntity>> GetWorkflowsByMailServerConfigurationIdAsync(string tenantId, string mailServerConfigurationId);

        Task<List<WorkflowEntity>> GetWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);

        Task<List<WorkflowEntity>> GetPublishWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);

        Task<long> GetWorkflowsCountAsync(string tenantId, string? search, bool? isPublished);

        Task<WorkflowEntity> GetWorkflowAsync(string tenantId, string workflowId);

        Task UpdateWorkflowAsync(WorkflowEntity workflow);

        Task DeleteWorkflowAsync(string tenantId, string workflowId);
    }
}