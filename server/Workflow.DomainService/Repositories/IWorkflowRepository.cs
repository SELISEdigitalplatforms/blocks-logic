using Workflow.DomainService.Entities;

namespace Workflow.DomainService.Repositories
{
    public interface IWorkflowRepository
    {
        Task CreateWorkflowAsync(WorkflowEntity workflow);

        Task<List<WorkflowEntity>> GetAllWorkflowsAsync(string tenantId, int pageSize, int pageNumber, string? search, bool? isPublished);

        Task<List<WorkflowEntity>> GetWorkflowsByMailServerConfigurationIdAsync(string tenantId, string mailServerConfigurationId);

        Task<List<WorkflowEntity>> GetWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);

        Task<List<WorkflowEntity>> GetPublishWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation);

        Task<long> GetWorkflowsCountAsync(string tenantId, string? search, bool? isPublished);

        /// <summary>
        /// Workflows with a function step pointing at <paramref name="functionId"/>, so deleting a
        /// function can say what it would break. Projected to identity only — the caller wants
        /// names, not definitions.
        /// </summary>
        Task<List<WorkflowEntity>> GetWorkflowsUsingFunctionAsync(string tenantId, string functionId, int limit = 50);

        Task<WorkflowEntity> GetWorkflowAsync(string tenantId, string workflowId);

        Task UpdateWorkflowAsync(WorkflowEntity workflow);

        Task DeleteWorkflowAsync(string tenantId, string workflowId);
    }
}