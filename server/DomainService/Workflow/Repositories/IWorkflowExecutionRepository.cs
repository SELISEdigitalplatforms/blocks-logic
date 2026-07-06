using DomainService.Workflow.Enums;
using DomainService.Workflow.Models;
using MongoDB.Bson;

namespace DomainService.Workflow.Repositories
{
    public interface IWorkflowExecutionRepository
    {
        Task<WorkflowExecutionModel> CreateAsync(WorkflowExecutionModel execution);
        Task<WorkflowExecutionModel?> GetByIdAsync(string id, string tenantId);
        Task UpdateAsync(WorkflowExecutionModel execution);
        Task<bool> AtomicCompleteNodeAsync(string executionId, string tenantId, string completedNodeId, List<string> nextNodeIds);
        Task AtomicFinalizeExecutionAsync(string executionId, string tenantId);
        Task AtomicAddNodeExecutionAsync(string executionId, string tenantId, NodeExecutionModel nodeExecution);
        Task AtomicUpdateNodeExecutionCompletedAsync(string executionId, string tenantId, string nodeExecutionId, int outputItemCount, Dictionary<string, int> outputCountsByBranch, BsonDocument? contextUpdates);
        Task AtomicUpdateNodeExecutionFailedAsync(string executionId, string tenantId, string nodeExecutionId, string error);
        Task<List<WorkflowExecutionModel>> GetByWorkflowIdAsync(string workflowId, string tenantId);

        // Item-based execution methods
        Task AddItemsAsync(string tenantId, List<WorkflowItemExecutionModel> items);
        Task<List<WorkflowItemExecutionModel>> GetItemsByNodeIdsAsync(
            string workflowExecutionId,
            List<Dictionary<string, string>> nodeIdBranchPairs,
            string tenantId);
        Task<List<WorkflowItemExecutionModel>> GetAllItemsByExecutionIdAsync(
            string workflowExecutionId,
            string tenantId);

        Task<List<WorkflowItemExecutionModel>> GetAllItemsByNodeExecutionIdAsync(string nodeExecutionId, string tenantId);

        Task<WorkflowExecutionModel> GetLastCompletedExecution(string tenantId, string workflowId);
    }
}
