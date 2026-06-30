using Blocks.Genesis;
using DomainService.Workflow.Enums;
using DomainService.Workflow.Models;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Repositories
{
    [ExcludeFromCodeCoverage]
    public class WorkflowExecutionRepository : IWorkflowExecutionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "WorkflowExecutions";

        public WorkflowExecutionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;

        }

        /// <summary>
        /// Gets MongoDB collection for specific tenant.
        /// Uses tenantId from execution model for proper multi-tenancy.
        /// </summary>
        private IMongoCollection<WorkflowExecutionModel> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowExecutionModel>(tenantId, _collectionName);
        }

        public async Task<WorkflowExecutionModel> CreateAsync(WorkflowExecutionModel execution)
        {
            if (string.IsNullOrEmpty(execution.TenantId))
                throw new InvalidOperationException("TenantId is required for execution");

            var collection = GetCollection(execution.TenantId);
            await collection.InsertOneAsync(execution);
            return execution;
        }

        public async Task<WorkflowExecutionModel?> GetByIdAsync(string id, string tenantId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task UpdateAsync(WorkflowExecutionModel execution)
        {
            if (string.IsNullOrEmpty(execution.TenantId))
                throw new InvalidOperationException("TenantId is required for execution");

            var collection = GetCollection(execution.TenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, execution.Id);
            await collection.ReplaceOneAsync(filter, execution);
        }

        /// <summary>
        /// Atomically removes the completed node from ActiveNodeIds and adds next nodes.
        /// Uses MongoDB $pull and $addToSet to avoid race conditions in parallel execution.
        /// Returns true if ActiveNodeIds is empty after the update (workflow complete).
        /// </summary>
        public async Task<bool> AtomicCompleteNodeAsync(string executionId, string tenantId, string completedNodeId, List<string> nextNodeIds)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, executionId);

            // Step 1: Pull completed node
            var pullUpdate = Builders<WorkflowExecutionModel>.Update.Pull(e => e.ActiveNodeIds, completedNodeId);
            await collection.UpdateOneAsync(filter, pullUpdate);

            // Step 2: Add next nodes (only if not already present)
            if (nextNodeIds.Any())
            {
                var addUpdate = Builders<WorkflowExecutionModel>.Update.AddToSetEach(e => e.ActiveNodeIds, nextNodeIds);
                await collection.UpdateOneAsync(filter, addUpdate);
            }

            // Step 3: Check if ActiveNodeIds is now empty and mark complete atomically
            var completeFilter = Builders<WorkflowExecutionModel>.Filter.And(
                Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, executionId),
                Builders<WorkflowExecutionModel>.Filter.Size(e => e.ActiveNodeIds, 0)
            );
            var completeUpdate = Builders<WorkflowExecutionModel>.Update
                .Set(e => e.Status, WorkflowExecutionStatus.Completed)
                .Set(e => e.FinishedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(completeFilter, completeUpdate);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Atomically clears ActiveNodeIds and marks the execution as Completed with FinishedAt=now.
        /// Used by step-mode execution to guarantee a clean end state after the target node runs,
        /// regardless of whether step-mode left downstream node IDs in ActiveNodeIds (non-leaf targets).
        /// Idempotent: safe to call when the execution has already been auto-finalized by AtomicCompleteNodeAsync.
        /// </summary>
        public async Task AtomicFinalizeExecutionAsync(string executionId, string tenantId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, executionId);
            var update = Builders<WorkflowExecutionModel>.Update
                .Set(e => e.ActiveNodeIds, new List<string>())
                .Set(e => e.Status, WorkflowExecutionStatus.Completed)
                .Set(e => e.FinishedAt, DateTime.UtcNow);

            await collection.UpdateOneAsync(filter, update);
        }

        /// <summary>
        /// Atomically pushes a new NodeExecution to the NodeExecutions array and sets Status=Running.
        /// Prevents concurrent ReplaceOneAsync from overwriting other nodes' executions.
        /// </summary>
        public async Task AtomicAddNodeExecutionAsync(string executionId, string tenantId, NodeExecutionModel nodeExecution)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, executionId);

            var update = Builders<WorkflowExecutionModel>.Update
                .Push(e => e.NodeExecutions, nodeExecution)
                .Set(e => e.Status, WorkflowExecutionStatus.Running);

            await collection.UpdateOneAsync(filter, update);
        }

        /// <summary>
        /// Atomically updates a specific NodeExecution entry to Completed status with output metadata.
        /// Uses array filter to target the specific NodeExecution by its Id.
        /// </summary>
        public async Task AtomicUpdateNodeExecutionCompletedAsync(string executionId, string tenantId, string nodeExecutionId, int outputItemCount, Dictionary<string, int> outputCountsByBranch, BsonDocument? contextUpdates)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.And(
                Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, executionId),
                Builders<WorkflowExecutionModel>.Filter.ElemMatch(e => e.NodeExecutions, ne => ne.Id == nodeExecutionId)
            );

            var update = Builders<WorkflowExecutionModel>.Update
                .Set("NodeExecutions.$.Status", NodeExecutionStatus.Completed)
                .Set("NodeExecutions.$.OutputItemCount", outputItemCount)
                .Set("NodeExecutions.$.OutputCountsByBranch", outputCountsByBranch)
                .Set("NodeExecutions.$.EndedAt", DateTime.UtcNow);

            // Merge context updates into the execution's Context document
            if (contextUpdates != null)
            {
                foreach (var element in contextUpdates)
                {
                    update = update.Set($"Context.{element.Name}", element.Value);
                }
            }

            await collection.UpdateOneAsync(filter, update);
        }

        /// <summary>
        /// Atomically updates a specific NodeExecution entry to Failed status.
        /// Also marks the workflow execution as Failed.
        /// </summary>
        public async Task AtomicUpdateNodeExecutionFailedAsync(string executionId, string tenantId, string nodeExecutionId, string error)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.And(
                Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, executionId),
                Builders<WorkflowExecutionModel>.Filter.ElemMatch(e => e.NodeExecutions, ne => ne.Id == nodeExecutionId)
            );

            var update = Builders<WorkflowExecutionModel>.Update
                .Set("NodeExecutions.$.Status", NodeExecutionStatus.Failed)
                .Set("NodeExecutions.$.EndedAt", DateTime.UtcNow)
                .Set("NodeExecutions.$.Error", error)
                .Set(e => e.Status, WorkflowExecutionStatus.Failed)
                .Set(e => e.ErrorMessage, error)
                .Set(e => e.FinishedAt, DateTime.UtcNow);

            await collection.UpdateOneAsync(filter, update);
        }

        public async Task<List<WorkflowExecutionModel>> GetByWorkflowIdAsync(string workflowId, string tenantId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionModel>.Filter.Eq(e => e.WorkflowId, workflowId);
            return await collection.Find(filter)
                .SortByDescending(e => e.StartedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Add workflow item execution models to the database.
        /// These represent individual data items flowing through nodes.
        /// </summary>
        public async Task AddItemsAsync(string tenantId, List<WorkflowItemExecutionModel> items)
        {
            if (!items.Any()) return;
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionModel>(tenantId, "WorkflowItemExecutions");
            await collection.InsertManyAsync(items);
        }

        /// <summary>
        /// Get workflow items by node IDs for a specific execution.
        /// Used to resolve input items when executing downstream nodes.
        /// </summary>
        public async Task<List<WorkflowItemExecutionModel>> GetItemsByNodeIdsAsync(
            string workflowExecutionId,
            List<Dictionary<string, string>> nodeIdBranchPairs,
            string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionModel>(tenantId, "WorkflowItemExecutions");

            var filter = Builders<WorkflowItemExecutionModel>.Filter.And(
                Builders<WorkflowItemExecutionModel>.Filter.Eq("WorkflowExecutionId", workflowExecutionId),
                Builders<WorkflowItemExecutionModel>.Filter.Or(
                    nodeIdBranchPairs.Select(pair =>
                        Builders<WorkflowItemExecutionModel>.Filter.And(
                            Builders<WorkflowItemExecutionModel>.Filter.Eq("NodeId", pair["NodeId"]),
                            Builders<WorkflowItemExecutionModel>.Filter.Eq("Branch", pair["Branch"])
                        )
                    )
                )
            );

            var items = await collection.Find(filter).ToListAsync();
            return items;
        }

        /// <summary>
        /// Get all workflow items for a specific execution.
        /// Returns all data items that flowed through the workflow.
        /// Frontend will organize these into node input/output structure.
        /// </summary>
        public async Task<List<WorkflowItemExecutionModel>> GetAllItemsByExecutionIdAsync(string workflowExecutionId, string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionModel>(tenantId, "WorkflowItemExecutions");

            var filter = Builders<WorkflowItemExecutionModel>.Filter.Eq("WorkflowExecutionId", workflowExecutionId);

            var items = await collection.Find(filter).SortBy(doc => doc.CreatedAt).ToListAsync();
            return items;
        }


        public async Task<List<WorkflowItemExecutionModel>> GetAllItemsByNodeExecutionIdAsync(string nodeExecutionId, string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionModel>(tenantId, "WorkflowItemExecutions");

            var filter = Builders<WorkflowItemExecutionModel>.Filter.Eq("NodeExecutionId", nodeExecutionId);

            var items = await collection.Find(filter).SortBy(doc => doc.ItemIndex).ToListAsync();
            return items;
        }
    }
}
