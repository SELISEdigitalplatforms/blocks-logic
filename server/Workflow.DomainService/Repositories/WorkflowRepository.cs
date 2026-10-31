using Blocks.Genesis;
using DataAccess.InternalService;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Utils;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Repositories
{
    [ExcludeFromCodeCoverage]
    public class WorkflowRepository : IWorkflowRepository
    {

        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "Workflows";

        private readonly ILogicDataAccess _dataAccess;

        public WorkflowRepository(IDbContextProvider dbContextProvider, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;

        }

        /// <summary>
        /// Gets MongoDB collection for specific tenant.
        /// Uses tenantId for proper multi-tenancy support.
        /// </summary>
        private IMongoCollection<WorkflowEntity> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowEntity>(tenantId, _collectionName);
        }

        private Task<ReadScope<WorkflowEntity>> ReadAsync(string tenantId, FilterDefinition<WorkflowEntity> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.WorkflowEntity, filter, $"WorkflowRepository.{callSite}", tenantId, useProjection);

        public async Task CreateWorkflowAsync(WorkflowEntity workflow)
        {
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.WorkflowEntity, "WorkflowRepository.CreateWorkflow", workflow.TenantId))
                throw new UnauthorizedAccessException("Workflow create is not allowed by the data access policy.");
            await _dataAccess.PrepareWriteAsync(LogicSchemas.WorkflowEntity, workflow, isInsert: true, "WorkflowRepository.CreateWorkflow", workflow.TenantId);
            var collection = GetCollection(workflow.TenantId);
            await collection.InsertOneAsync(workflow, null);
        }

        public async Task<long> GetWorkflowsCountAsync(string tenantId, string? search, bool? isPublished)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowEntity>.Filter.Empty;
            if (!string.IsNullOrEmpty(search))
            {
                filter &= Builders<WorkflowEntity>.Filter.Regex(w => w.Name, new MongoDB.Bson.BsonRegularExpression(search, "i"));
            }
            if (isPublished.HasValue)
            {
                if (isPublished.Value)
                {
                    filter &= Builders<WorkflowEntity>.Filter.Eq(w => w.IsPublished, true);
                }
                else
                {
                    filter &= Builders<WorkflowEntity>.Filter.Or(
                        Builders<WorkflowEntity>.Filter.Eq(w => w.IsPublished, false),
                        Builders<WorkflowEntity>.Filter.Exists(w => w.IsPublished, false)
                    );
                }
            }
            var scope = await ReadAsync(tenantId, filter, "GetWorkflowsCount");
            return scope.Granted ? await collection.CountDocumentsAsync(scope.Filter) : 0;
        }

        public async Task<List<WorkflowEntity>> GetAllWorkflowsAsync(string tenantId, int pageSize, int pageNumber, string? search, bool? isPublished)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowEntity>.Filter.Empty;
            if (!string.IsNullOrEmpty(search))
            {
                filter &= Builders<WorkflowEntity>.Filter.Regex(w => w.Name, new MongoDB.Bson.BsonRegularExpression(search, "i"));
            }
            if (isPublished.HasValue)
            {
                if (isPublished.Value)
                {
                    filter &= Builders<WorkflowEntity>.Filter.Eq(w => w.IsPublished, true);
                }
                else
                {
                    filter &= Builders<WorkflowEntity>.Filter.Or(
                        Builders<WorkflowEntity>.Filter.Eq(w => w.IsPublished, false),
                        Builders<WorkflowEntity>.Filter.Exists(w => w.IsPublished, false)
                    );
                }
            }
            var scope = await ReadAsync(tenantId, filter, "GetAllWorkflows");
            if (!scope.Granted) return [];
            return scope.ForView(await scope.Apply(collection)
                .SortByDescending(x => x.CreatedDate)
                .Skip(pageNumber * pageSize)
                .Limit(pageSize).ToListAsync());
        }

        public async Task<WorkflowEntity> GetWorkflowAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowEntity>.Filter.Eq(w => w.ItemId, workflowId);
            // Entity may be written back by the caller (ReplaceOne): read it whole, no projection.
            var scope = await ReadAsync(tenantId, filter, "GetWorkflow", useProjection: false);
            return scope.Granted ? await scope.Apply(collection).FirstOrDefaultAsync() : null!;
        }

        public async Task<WorkflowEntity> GetWorkflowForViewAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowEntity>.Filter.Eq(w => w.ItemId, workflowId);
            // Masked: View column (property) rules clear what the caller may not see.
            var scope = await ReadAsync(tenantId, filter, "GetWorkflowForView");
            return scope.Granted ? scope.ForView(await scope.Apply(collection).FirstOrDefaultAsync()) : null!;
        }

        public async Task<List<WorkflowEntity>> GetWorkflowsByMailServerConfigurationIdAsync(string tenantId, string mailServerConfigurationId)
        {
            var collection = GetCollection(tenantId);

            // Draft match: test mode reads the draft's email node (and its test subject).
            var draftMatch = Builders<WorkflowEntity>.Filter.ElemMatch(w => w.Nodes,
                Builders<NodeEntity>.Filter.Eq("Parameters.mailServerConfigurationId", mailServerConfigurationId));

            // Published match: production must follow the PUBLISHED version. Its trigger nodes are copied into
            // PublishedMeta.TriggerNodes at publish time, so a workflow whose draft no longer uses this mailbox
            // (edited, not yet re-published) is still found. The caller re-checks the published snapshot.
            var publishedNode = Builders<NodeEntity>.Filter.And(
                Builders<NodeEntity>.Filter.Eq("Parameters.mailServerConfigurationId", mailServerConfigurationId),
                Builders<NodeEntity>.Filter.Eq("Type", "email"),
                Builders<NodeEntity>.Filter.Eq("Category", "trigger"));
            var publishedMatch = Builders<WorkflowEntity>.Filter.And(
                Builders<WorkflowEntity>.Filter.Eq(w => w.IsPublished, true),
                Builders<WorkflowEntity>.Filter.ElemMatch(w => w.PublishedMeta.TriggerNodes, publishedNode));

            var filter = Builders<WorkflowEntity>.Filter.Or(draftMatch, publishedMatch);
            var scope = await ReadAsync(tenantId, filter, "GetWorkflowsByMailServerConfiguration", useProjection: false);
            return scope.Granted ? await scope.Apply(collection).ToListAsync() : [];
        }

        public async Task<List<WorkflowEntity>> GetWorkflowsUsingFunctionAsync(
            string tenantId, string functionId, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(functionId)) return [];

            var collection = GetCollection(tenantId);

            // Node parameters are written by the editor, so the key is camelCase — the same shape
            // GetWorkflowsByDataCollectionAsync matches on. A function step is an action, so only
            // Nodes is searched; it can never sit in the trigger lists.
            var nodeFilter = Builders<NodeEntity>.Filter.And(
                Builders<NodeEntity>.Filter.Eq("Parameters.functionId", functionId),
                Builders<NodeEntity>.Filter.Eq("Type", "function")
            );

            var filter = Builders<WorkflowEntity>.Filter.And(
                Builders<WorkflowEntity>.Filter.Eq(w => w.TenantId, tenantId),
                Builders<WorkflowEntity>.Filter.ElemMatch(w => w.Nodes, nodeFilter)
            );

            var projection = Builders<WorkflowEntity>.Projection
                .Include(w => w.ItemId)
                .Include(w => w.Name)
                .Include(w => w.TenantId)
                .Include(w => w.IsPublished);

            // Own projection kept (UI list); only the resolver row filter is added.
            var scope = await ReadAsync(tenantId, filter, "GetWorkflowsUsingFunction", useProjection: false);
            if (!scope.Granted) return [];
            return await collection.Find(scope.Filter)
                .Project<WorkflowEntity>(projection)
                .Limit(Math.Max(1, limit))
                .ToListAsync();
        }

        public async Task<List<WorkflowEntity>> GetWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation)
        {
            var collection = GetCollection(tenantId);

            var nodeFilter = Builders<NodeEntity>.Filter.And(
                Builders<NodeEntity>.Filter.Eq("Parameters.collectionName", collectionName),
                Builders<NodeEntity>.Filter.Eq("Parameters.operation", operation),
                Builders<NodeEntity>.Filter.Eq("Type", "dataGateway"),
                Builders<NodeEntity>.Filter.Eq("Category", "trigger")
            );

            var filter = Builders<WorkflowEntity>.Filter.And(
                Builders<WorkflowEntity>.Filter.ElemMatch(w => w.Nodes, nodeFilter)
            );

            var scope = await ReadAsync(tenantId, filter, "GetWorkflowsByDataCollection", useProjection: false);
            return scope.Granted ? await scope.Apply(collection).ToListAsync() : [];
        }

        public async Task<List<WorkflowEntity>> GetPublishWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation)
        {
            var collection = GetCollection(tenantId);

            var nodeFilter = Builders<NodeEntity>.Filter.And(
                Builders<NodeEntity>.Filter.Eq("Parameters.collectionName", collectionName),
                Builders<NodeEntity>.Filter.Eq("Parameters.operation", operation),
                Builders<NodeEntity>.Filter.Eq("Type", "dataGateway"),
                Builders<NodeEntity>.Filter.Eq("Category", "trigger")
            );

            var filter = Builders<WorkflowEntity>.Filter.And(
                Builders<WorkflowEntity>.Filter.Eq(w => w.IsPublished, true),
                Builders<WorkflowEntity>.Filter.ElemMatch(w => w.PublishedMeta.TriggerNodes, nodeFilter)
            );

            var scope = await ReadAsync(tenantId, filter, "GetPublishWorkflowsByDataCollection", useProjection: false);
            return scope.Granted ? await scope.Apply(collection).ToListAsync() : [];
        }

        /// <summary>
        /// Published only, matched on the published trigger nodes (<c>PublishedMeta.TriggerNodes</c>), never on
        /// the draft graph: a draft change to the event does not take effect until it is published.
        /// </summary>
        public static FilterDefinition<WorkflowEntity> PublishedBlocksEventFilter(string eventName)
        {
            var nodeFilter = Builders<NodeEntity>.Filter.And(
                Builders<NodeEntity>.Filter.Eq("Parameters.eventName", eventName),
                Builders<NodeEntity>.Filter.Eq("Type", LogicConstants.BlocksEventNodeType),
                Builders<NodeEntity>.Filter.Eq("Category", "trigger")
            );

            return Builders<WorkflowEntity>.Filter.And(
                Builders<WorkflowEntity>.Filter.Eq(w => w.IsPublished, true),
                Builders<WorkflowEntity>.Filter.ElemMatch(w => w.PublishedMeta.TriggerNodes, nodeFilter)
            );
        }

        public async Task<List<WorkflowEntity>> GetPublishWorkflowsByBlocksEventAsync(string tenantId, string eventName)
        {
            var collection = GetCollection(tenantId);
            var scope = await ReadAsync(tenantId, PublishedBlocksEventFilter(eventName), "GetPublishWorkflowsByBlocksEvent", useProjection: false);
            return scope.Granted ? await scope.Apply(collection).ToListAsync() : [];
        }

        public async Task<long> CountPublishWorkflowsByBlocksEventAsync(string tenantId, string eventName)
        {
            var collection = GetCollection(tenantId);
            var scope = await ReadAsync(tenantId, PublishedBlocksEventFilter(eventName), "CountPublishWorkflowsByBlocksEvent");
            return scope.Granted ? await collection.CountDocumentsAsync(scope.Filter) : 0;
        }

        public async Task UpdateWorkflowAsync(WorkflowEntity workflow)
        {
            if (string.IsNullOrEmpty(workflow.TenantId))
                throw new InvalidOperationException("TenantId is required for workflow");

            var collection = GetCollection(workflow.TenantId);
            var filter = await _dataAccess.EditFilterAsync(LogicSchemas.WorkflowEntity,
                Builders<WorkflowEntity>.Filter.Eq(item => item.ItemId, workflow.ItemId), "WorkflowRepository.UpdateWorkflow", workflow.TenantId);
            if (filter is not null)
                await _dataAccess.PrepareReplaceAsync(LogicSchemas.WorkflowEntity, workflow, collection, filter, "WorkflowRepository.UpdateWorkflow", workflow.TenantId);
            // Denied: no write; the existing "not found" error is the answer.
            var result = filter is null ? null : await collection.ReplaceOneAsync(filter, workflow);
            if (result is null || result.MatchedCount == 0)
            {
                throw new KeyNotFoundException($"Workflow with ItemId '{workflow.ItemId}' not found");
            }
        }

        public async Task DeleteWorkflowAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.WorkflowEntity,
                Builders<WorkflowEntity>.Filter.Eq(w => w.ItemId, workflowId), "WorkflowRepository.DeleteWorkflow", tenantId);
            if (filter is null) return;
            await collection.DeleteOneAsync(filter);
        }

    }
}