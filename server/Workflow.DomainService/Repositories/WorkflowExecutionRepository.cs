using Blocks.Genesis;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Services;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;

namespace Workflow.DomainService.Repositories
{
    [ExcludeFromCodeCoverage]
    public class WorkflowExecutionRepository : IWorkflowExecutionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "WorkflowExecutions";
        private const string ListIndexName = "ix_workflowexec_workflow_started_id";
        private readonly HashSet<string> _indexedTenants = new(StringComparer.Ordinal);
        private readonly Lock _indexGate = new();

        private static readonly ProjectionDefinition<WorkflowExecutionEntity> ListProjection =
            Builders<WorkflowExecutionEntity>.Projection
                .Include(e => e.Id)
                .Include(e => e.WorkflowId)
                .Include(e => e.WorkflowName)
                .Include(e => e.Status)
                .Include(e => e.ExecutionMode)
                .Include(e => e.StartedAt)
                .Include(e => e.FinishedAt)
                .Include(e => e.ErrorMessage)
                .Include(e => e.AttemptNumber);

        private static readonly SortDefinition<WorkflowExecutionEntity> NewestFirst =
            Builders<WorkflowExecutionEntity>.Sort
                .Descending(e => e.StartedAt)
                .Descending(e => e.Id);

        private static readonly SortDefinition<WorkflowExecutionEntity> OldestFirst =
            Builders<WorkflowExecutionEntity>.Sort
                .Ascending(e => e.StartedAt)
                .Ascending(e => e.Id);

        public WorkflowExecutionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;

        }

        /// <summary>
        /// Gets MongoDB collection for specific tenant.
        /// Uses tenantId from execution model for proper multi-tenancy.
        /// </summary>
        private IMongoCollection<WorkflowExecutionEntity> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowExecutionEntity>(tenantId, _collectionName);
        }

        public async Task<WorkflowExecutionEntity> CreateAsync(WorkflowExecutionEntity execution)
        {
            if (string.IsNullOrEmpty(execution.TenantId))
                throw new InvalidOperationException("TenantId is required for execution");

            var collection = GetCollection(execution.TenantId);
            await EnsureIndexesAsync(execution.TenantId);
            await collection.InsertOneAsync(execution);
            return execution;
        }

        public async Task<WorkflowExecutionEntity?> GetByIdAsync(string id, string tenantId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, id);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task UpdateAsync(WorkflowExecutionEntity execution)
        {
            if (string.IsNullOrEmpty(execution.TenantId))
                throw new InvalidOperationException("TenantId is required for execution");

            var collection = GetCollection(execution.TenantId);
            var filter = Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, execution.Id);
            await collection.ReplaceOneAsync(filter, execution);
        }

        /// <summary>
        /// Removes the finished node, adds its children, and marks the execution Completed
        /// when the active set is empty. One document update, so a parallel leaf cannot
        /// observe the empty set before the children are added.
        /// Returns true only when this update is the one that moves a non-failed execution to Completed.
        /// </summary>
        public async Task<bool> AtomicCompleteNodeAsync(string executionId, string tenantId, string completedNodeId, List<string> nextNodeIds)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, executionId);
            var update = Builders<WorkflowExecutionEntity>.Update.Pipeline(CompletionPipeline(completedNodeId, nextNodeIds));
            var before = await collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<WorkflowExecutionEntity> { ReturnDocument = ReturnDocument.Before });

            if (before == null)
            {
                return false;
            }

            return ActiveNodeCompletion.Apply(
                before.ActiveNodeIds,
                completedNodeId,
                nextNodeIds,
                before.Status).BecameComplete;
        }

        /// <summary>
        /// Stored form of <see cref="ActiveNodeCompletion.Apply"/>. The second stage sees the
        /// active set written by the first. FinishedAt changes only on the transition to Completed.
        /// </summary>
        private static PipelineDefinition<WorkflowExecutionEntity, WorkflowExecutionEntity> CompletionPipeline(
            string completedNodeId,
            IReadOnlyCollection<string>? nextNodeIds)
        {
            var nextIds = new BsonArray();
            if (nextNodeIds != null)
            {
                foreach (var id in nextNodeIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        nextIds.Add(id);
                    }
                }
            }

            var stillActive = new BsonDocument("$setUnion", new BsonArray
            {
                new BsonDocument("$filter", new BsonDocument
                {
                    { "input", new BsonDocument("$ifNull", new BsonArray { "$ActiveNodeIds", new BsonArray() }) },
                    { "as", "id" },
                    { "cond", new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$ne", new BsonArray { "$$id", completedNodeId ?? "" }),
                            new BsonDocument("$gt", new BsonArray
                            {
                                new BsonDocument("$strLenCP", new BsonDocument("$trim", new BsonDocument("input", new BsonDocument("$ifNull", new BsonArray { "$$id", "" })))),
                                0
                            })
                        })
                    }
                }),
                nextIds
            });

            var becomesComplete = new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { new BsonDocument("$size", "$ActiveNodeIds"), 0 }),
                new BsonDocument("$ne", new BsonArray { "$Status", (int)WorkflowExecutionStatus.Failed }),
                new BsonDocument("$ne", new BsonArray { "$Status", (int)WorkflowExecutionStatus.Completed })
            });

            return new EmptyPipelineDefinition<WorkflowExecutionEntity>()
                .AppendStage<WorkflowExecutionEntity, WorkflowExecutionEntity, WorkflowExecutionEntity>(
                    new BsonDocument("$set", new BsonDocument("ActiveNodeIds", stillActive)))
                .AppendStage<WorkflowExecutionEntity, WorkflowExecutionEntity, WorkflowExecutionEntity>(
                    new BsonDocument("$set", new BsonDocument
                    {
                        { "Status", new BsonDocument("$cond", new BsonArray { becomesComplete, (int)WorkflowExecutionStatus.Completed, "$Status" }) },
                        { "FinishedAt", new BsonDocument("$cond", new BsonArray { becomesComplete, new BsonDateTime(DateTime.UtcNow), "$FinishedAt" }) }
                    }));
        }

        /// <summary>
        /// Atomically clears ActiveNodeIds and marks the execution as Completed with FinishedAt=now.
        /// Used by step-mode execution to guarantee a clean end state after the target node runs,
        /// regardless of whether step-mode left downstream node IDs in ActiveNodeIds (non-leaf targets).
        /// Idempotent: safe to call when the execution has already been auto-finalized by AtomicCompleteNodeAsync.
        /// </summary>
        public async Task<bool> AtomicFinalizeExecutionAsync(string executionId, string tenantId)
        {
            var collection = GetCollection(tenantId);
            // A Failed execution stays Failed: another branch may have failed while this one reached its target.
            var filter = Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, executionId) & NotFailed;
            var update = Builders<WorkflowExecutionEntity>.Update
                .Set(e => e.ActiveNodeIds, new List<string>())
                .Set(e => e.Status, WorkflowExecutionStatus.Completed)
                .Set(e => e.FinishedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.MatchedCount > 0;
        }

        /// <summary>
        /// Every write that sets an execution Running or Completed carries this. Without it a branch that
        /// read the execution before another branch failed it set it back to Running, and the run then
        /// ended "Completed" with a failed node in it.
        /// </summary>
        public static readonly FilterDefinition<WorkflowExecutionEntity> NotFailed =
            Builders<WorkflowExecutionEntity>.Filter.Ne(e => e.Status, WorkflowExecutionStatus.Failed);

        /// <summary>
        /// Atomically pushes a new NodeExecution to the NodeExecutions array and sets Status=Running.
        /// Prevents concurrent ReplaceOneAsync from overwriting other nodes' executions.
        /// </summary>
        public async Task AtomicAddNodeExecutionAsync(string executionId, string tenantId, NodeExecutionEntity nodeExecution)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, executionId);

            var update = Builders<WorkflowExecutionEntity>.Update
                .Push(e => e.NodeExecutions, nodeExecution)
                .Set(e => e.Status, WorkflowExecutionStatus.Running);

            await collection.UpdateOneAsync(filter, update);
        }

        public async Task<bool> TryReopenFailedExecutionAsync(string executionId, string tenantId)
        {
            var filter = Builders<WorkflowExecutionEntity>.Filter;
            var update = Builders<WorkflowExecutionEntity>.Update
                .Set(e => e.Status, WorkflowExecutionStatus.Running)
                .Set(e => e.ErrorMessage, null)
                .Set(e => e.FinishedAt, null)
                .Inc(e => e.AttemptNumber, 1);

            var result = await GetCollection(tenantId).UpdateOneAsync(
                filter.Eq(e => e.Id, executionId)
                & filter.Eq(e => e.Status, WorkflowExecutionStatus.Failed)
                & filter.Eq(e => e.ExecutionMode, WorkflowExecutionMode.Production),
                update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> TryAddFirstNodeExecutionAsync(string executionId, string tenantId, NodeExecutionEntity nodeExecution)
        {
            var update = Builders<WorkflowExecutionEntity>.Update
                .Push(e => e.NodeExecutions, nodeExecution)
                .Set(e => e.Status, WorkflowExecutionStatus.Running);

            var result = await GetCollection(tenantId).UpdateOneAsync(FirstRunFilter(executionId, nodeExecution.NodeId) & NotFailed, update);
            return result.MatchedCount > 0;
        }

        public async Task<bool> TryAddNodeExecutionAsync(string executionId, string tenantId, NodeExecutionEntity nodeExecution)
        {
            var update = Builders<WorkflowExecutionEntity>.Update
                .Push(e => e.NodeExecutions, nodeExecution)
                .Set(e => e.Status, WorkflowExecutionStatus.Running);

            var result = await GetCollection(tenantId).UpdateOneAsync(
                Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, executionId) & NotFailed, update);
            return result.MatchedCount > 0;
        }

        /// <summary>
        /// The execution, only while it has no Running or Completed row for <paramref name="nodeId"/>. One
        /// document, one update: two deliveries of the same node message cannot both pass it.
        /// </summary>
        public static FilterDefinition<WorkflowExecutionEntity> FirstRunFilter(string executionId, string nodeId)
        {
            var filter = Builders<WorkflowExecutionEntity>.Filter;
            return filter.Eq(e => e.Id, executionId)
                & filter.Not(filter.ElemMatch(e => e.NodeExecutions, ne => ne.NodeId == nodeId
                    && (ne.Status == NodeExecutionStatus.Running || ne.Status == NodeExecutionStatus.Completed)));
        }

        /// <summary>
        /// Atomically updates a specific NodeExecution entry to Completed status with output metadata.
        /// Uses array filter to target the specific NodeExecution by its Id.
        /// </summary>
        public async Task AtomicUpdateNodeExecutionCompletedAsync(string executionId, string tenantId, string nodeExecutionId, int outputItemCount, Dictionary<string, int> outputCountsByBranch, BsonDocument? contextUpdates)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionEntity>.Filter.And(
                Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, executionId),
                Builders<WorkflowExecutionEntity>.Filter.ElemMatch(e => e.NodeExecutions, ne => ne.Id == nodeExecutionId)
            );

            var update = Builders<WorkflowExecutionEntity>.Update
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
        public async Task AtomicUpdateNodeExecutionFailedAsync(string executionId, string tenantId, string nodeExecutionId, string error, int outputItemCount, Dictionary<string, int> outputCountsByBranch)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionEntity>.Filter.And(
                Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.Id, executionId),
                Builders<WorkflowExecutionEntity>.Filter.ElemMatch(e => e.NodeExecutions, ne => ne.Id == nodeExecutionId)
            );

            var update = Builders<WorkflowExecutionEntity>.Update
                .Set("NodeExecutions.$.Status", NodeExecutionStatus.Failed)
                .Set("NodeExecutions.$.EndedAt", DateTime.UtcNow)
                .Set("NodeExecutions.$.Error", error)
                .Set("NodeExecutions.$.OutputItemCount", outputItemCount)
                .Set("NodeExecutions.$.OutputCountsByBranch", outputCountsByBranch)
                .Set(e => e.Status, WorkflowExecutionStatus.Failed)
                .Set(e => e.ErrorMessage, error)
                .Set(e => e.FinishedAt, DateTime.UtcNow);

            await collection.UpdateOneAsync(filter, update);
        }

        public async Task<List<WorkflowExecutionEntity>> GetByWorkflowIdAsync(string workflowId, string tenantId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.WorkflowId, workflowId);
            return await collection.Find(filter)
                .SortByDescending(e => e.StartedAt)
                .ToListAsync();
        }

        public Task<List<WorkflowExecutionListRow>> GetPageAsync(string workflowId, string tenantId, int pageSize)
        {
            return QueryListAsync(tenantId, WorkflowFilter(workflowId), NewestFirst, pageSize);
        }

        public Task<List<WorkflowExecutionListRow>> GetOlderThanAsync(
            string workflowId, string tenantId, DateTime startedAt, string id, int pageSize)
        {
            // Mirrors WorkflowExecutionListCursor.IsStrictlyOlder.
            var builder = Builders<WorkflowExecutionEntity>.Filter;
            var older = builder.Or(
                builder.Lt(e => e.StartedAt, startedAt),
                builder.And(
                    builder.Eq(e => e.StartedAt, startedAt),
                    builder.Lt(e => e.Id, id)));
            return QueryListAsync(tenantId, WorkflowFilter(workflowId) & older, NewestFirst, pageSize);
        }

        public Task<List<WorkflowExecutionListRow>> GetNewerThanAsync(
            string workflowId, string tenantId, DateTime startedAt, string id, int pageSize)
        {
            // Mirrors WorkflowExecutionListCursor.IsStrictlyNewer.
            // Ascending + limit returns the contiguous next chunk, not the newest PageSize
            // (which would skip runs when a burst is larger than the page).
            var builder = Builders<WorkflowExecutionEntity>.Filter;
            var newer = builder.Or(
                builder.Gt(e => e.StartedAt, startedAt),
                builder.And(
                    builder.Eq(e => e.StartedAt, startedAt),
                    builder.Gt(e => e.Id, id)));
            return QueryListAsync(tenantId, WorkflowFilter(workflowId) & newer, OldestFirst, pageSize);
        }

        public async Task<long> CountByWorkflowIdAsync(string workflowId, string tenantId)
        {
            await EnsureIndexesAsync(tenantId);
            return await GetCollection(tenantId).CountDocumentsAsync(WorkflowFilter(workflowId));
        }

        public async Task<List<WorkflowExecutionListRow>> GetListItemsByIdsAsync(
            string workflowId, string tenantId, IReadOnlyCollection<string> ids)
        {
            if (ids.Count == 0)
            {
                return [];
            }

            await EnsureIndexesAsync(tenantId);
            var builder = Builders<WorkflowExecutionEntity>.Filter;
            var filter = WorkflowFilter(workflowId) & builder.In(e => e.Id, ids);
            return await GetCollection(tenantId)
                .Find(filter)
                .Project<WorkflowExecutionListRow>(ListProjection)
                .ToListAsync();
        }

        private async Task<List<WorkflowExecutionListRow>> QueryListAsync(
            string tenantId,
            FilterDefinition<WorkflowExecutionEntity> filter,
            SortDefinition<WorkflowExecutionEntity> sort,
            int pageSize)
        {
            await EnsureIndexesAsync(tenantId);
            return await GetCollection(tenantId)
                .Find(filter)
                .Sort(sort)
                .Limit(pageSize)
                .Project<WorkflowExecutionListRow>(ListProjection)
                .ToListAsync();
        }

        private static FilterDefinition<WorkflowExecutionEntity> WorkflowFilter(string workflowId) =>
            Builders<WorkflowExecutionEntity>.Filter.Eq(e => e.WorkflowId, workflowId);

        private async Task EnsureIndexesAsync(string tenantId)
        {
            lock (_indexGate)
            {
                if (!_indexedTenants.Add(tenantId))
                {
                    return;
                }
            }

            try
            {
                var keys = Builders<WorkflowExecutionEntity>.IndexKeys;
                await GetCollection(tenantId).Indexes.CreateOneAsync(
                    new CreateIndexModel<WorkflowExecutionEntity>(
                        keys.Ascending(e => e.WorkflowId)
                            .Descending(e => e.StartedAt)
                            .Descending(e => e.Id),
                        new CreateIndexOptions { Name = ListIndexName }));
            }
            catch
            {
                lock (_indexGate)
                {
                    _indexedTenants.Remove(tenantId);
                }

                throw;
            }
        }

        /// <summary>
        /// Add workflow item execution models to the database.
        /// These represent individual data items flowing through nodes.
        /// </summary>
        public async Task AddItemsAsync(string tenantId, List<WorkflowItemExecutionEntity> items)
        {
            if (!items.Any()) return;
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionEntity>(tenantId, "WorkflowItemExecutions");
            await collection.InsertManyAsync(items);
        }

        /// <summary>
        /// Get workflow items by node IDs for a specific execution.
        /// Used to resolve input items when executing downstream nodes.
        /// </summary>
        public async Task<List<WorkflowItemExecutionEntity>> GetItemsByNodeIdsAsync(
            string workflowExecutionId,
            List<Dictionary<string, string>> nodeIdBranchPairs,
            string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionEntity>(tenantId, "WorkflowItemExecutions");

            var filter = Builders<WorkflowItemExecutionEntity>.Filter.And(
                Builders<WorkflowItemExecutionEntity>.Filter.Eq("WorkflowExecutionId", workflowExecutionId),
                Builders<WorkflowItemExecutionEntity>.Filter.Or(
                    nodeIdBranchPairs.Select(pair =>
                        Builders<WorkflowItemExecutionEntity>.Filter.And(
                            Builders<WorkflowItemExecutionEntity>.Filter.Eq("NodeId", pair["NodeId"]),
                            Builders<WorkflowItemExecutionEntity>.Filter.Eq("Branch", pair["Branch"])
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
        public async Task<List<WorkflowItemExecutionEntity>> GetAllItemsByExecutionIdAsync(string workflowExecutionId, string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionEntity>(tenantId, "WorkflowItemExecutions");

            var filter = Builders<WorkflowItemExecutionEntity>.Filter.Eq("WorkflowExecutionId", workflowExecutionId);

            var items = await collection.Find(filter).SortBy(doc => doc.CreatedAt).ToListAsync();
            return items;
        }


        public async Task<List<WorkflowItemExecutionEntity>> GetAllItemsByNodeExecutionIdAsync(string nodeExecutionId, string tenantId)
        {
            var collection = _dbContextProvider.GetCollection<WorkflowItemExecutionEntity>(tenantId, "WorkflowItemExecutions");

            var filter = Builders<WorkflowItemExecutionEntity>.Filter.Eq("NodeExecutionId", nodeExecutionId);

            var items = await collection.Find(filter).SortBy(doc => doc.ItemIndex).ToListAsync();
            return items;
        }

        public Task<WorkflowExecutionEntity> GetLastCompletedExecution(string tenantId, string workflowId)
        {
            var collection = _dbContextProvider.GetCollection<WorkflowExecutionEntity>(tenantId, _collectionName);
            return collection.Find(item =>
                item.TenantId == tenantId &&
                item.WorkflowId == workflowId &&
                item.ExecutionMode == WorkflowExecutionMode.Test &&
                item.Status == WorkflowExecutionStatus.Completed
            )
            .SortByDescending(item => item.FinishedAt)
            .FirstOrDefaultAsync();
        }
    }
}
