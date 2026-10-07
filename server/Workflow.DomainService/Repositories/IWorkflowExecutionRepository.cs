using Workflow.DomainService.Enums;
using Workflow.DomainService.Entities;
using MongoDB.Bson;

namespace Workflow.DomainService.Repositories
{
    public interface IWorkflowExecutionRepository
    {
        Task<WorkflowExecutionEntity> CreateAsync(WorkflowExecutionEntity execution);
        Task<WorkflowExecutionEntity?> GetByIdAsync(string id, string tenantId);
        Task UpdateAsync(WorkflowExecutionEntity execution);
        Task<bool> AtomicCompleteNodeAsync(string executionId, string tenantId, string completedNodeId, List<string> nextNodeIds);
        Task AtomicFinalizeExecutionAsync(string executionId, string tenantId);
        Task AtomicAddNodeExecutionAsync(string executionId, string tenantId, NodeExecutionEntity nodeExecution);
        /// <summary>
        /// Adds the node's row only if this execution has no Running or Completed row for that node yet,
        /// in one atomic update. False means the node already ran or is running (a redelivered message).
        /// </summary>
        Task<bool> TryAddFirstNodeExecutionAsync(string executionId, string tenantId, NodeExecutionEntity nodeExecution);

        /// <summary>
        /// Puts a failed production execution back to Running so it can continue, in one atomic update.
        /// False if it is not (or no longer) a failed production execution — so two resumes cannot both start.
        /// </summary>
        Task<bool> TryReopenFailedExecutionAsync(string executionId, string tenantId);
        Task AtomicUpdateNodeExecutionCompletedAsync(string executionId, string tenantId, string nodeExecutionId, int outputItemCount, Dictionary<string, int> outputCountsByBranch, BsonDocument? contextUpdates);
        Task AtomicUpdateNodeExecutionFailedAsync(string executionId, string tenantId, string nodeExecutionId, string error, int outputItemCount, Dictionary<string, int> outputCountsByBranch);
        Task<List<WorkflowExecutionEntity>> GetByWorkflowIdAsync(string workflowId, string tenantId);

        Task<List<WorkflowExecutionListRow>> GetPageAsync(string workflowId, string tenantId, int pageSize);

        Task<List<WorkflowExecutionListRow>> GetOlderThanAsync(string workflowId, string tenantId, DateTime startedAt, string id, int pageSize);

        Task<List<WorkflowExecutionListRow>> GetNewerThanAsync(string workflowId, string tenantId, DateTime startedAt, string id, int pageSize);

        Task<long> CountByWorkflowIdAsync(string workflowId, string tenantId);

        Task<List<WorkflowExecutionListRow>> GetListItemsByIdsAsync(string workflowId, string tenantId, IReadOnlyCollection<string> ids);

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
