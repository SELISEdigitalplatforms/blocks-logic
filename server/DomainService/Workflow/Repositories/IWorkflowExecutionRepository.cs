using DomainService.Workflow.Enums;
using DomainService.Workflow.Entities;
using MongoDB.Bson;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowExecutionRepository
    {
        Task<WorkflowExecutionEntity> CreateAsync(WorkflowExecutionEntity execution);
        Task<WorkflowExecutionEntity?> GetByIdAsync(string id, string tenantId);
        Task UpdateAsync(WorkflowExecutionEntity execution);
        Task<bool> AtomicCompleteNodeAsync(string executionId, string tenantId, string completedNodeId, List<string> nextNodeIds);
        Task AtomicFinalizeExecutionAsync(string executionId, string tenantId);
        Task AtomicAddNodeExecutionAsync(string executionId, string tenantId, NodeExecutionEntity nodeExecution);
        Task AtomicUpdateNodeExecutionCompletedAsync(string executionId, string tenantId, string nodeExecutionId, int outputItemCount, Dictionary<string, int> outputCountsByBranch, BsonDocument? contextUpdates);
        Task AtomicUpdateNodeExecutionFailedAsync(string executionId, string tenantId, string nodeExecutionId, string error);
        Task<List<WorkflowExecutionEntity>> GetByWorkflowIdAsync(string workflowId, string tenantId);

        // Item-based execution methods
        Task AddItemsAsync(string tenantId, List<WorkflowItemExecutionEntity> items);
        Task<List<WorkflowItemExecutionEntity>> GetItemsByNodeIdsAsync(
            string workflowExecutionId,
            List<Dictionary<string, string>> nodeIdBranchPairs,
            string tenantId);
        Task<List<WorkflowItemExecutionEntity>> GetAllItemsByExecutionIdAsync(
            string workflowExecutionId,
            string tenantId);

        Task<List<WorkflowItemExecutionEntity>> GetAllItemsByNodeExecutionIdAsync(string nodeExecutionId, string tenantId);

        Task<WorkflowExecutionEntity> GetLastCompletedExecution(string tenantId, string workflowId);
    }
}
