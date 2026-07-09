using Blocks.Genesis;
using DomainService.Workflow.Models;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Repositories
{
    [ExcludeFromCodeCoverage]
    public class WorkflowRepository : IWorkflowRepository
    {

        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "Workflows";

        public WorkflowRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;

        }

        /// <summary>
        /// Gets MongoDB collection for specific tenant.
        /// Uses tenantId for proper multi-tenancy support.
        /// </summary>
        private IMongoCollection<WorkflowModel> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowModel>(tenantId, _collectionName);
        }

        public async Task CreateWorkflowAsync(WorkflowModel workflow)
        {

            var collection = GetCollection(workflow.TenantId);
            await collection.InsertOneAsync(workflow, null);
        }

        public Task<long> GetWorkflowsCountAsync(string tenantId, string? search, bool? isPublished)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowModel>.Filter.Empty;
            if (!string.IsNullOrEmpty(search))
            {
                filter &= Builders<WorkflowModel>.Filter.Regex(w => w.Name, new MongoDB.Bson.BsonRegularExpression(search, "i"));
            }
            if (isPublished.HasValue)
            {
                if (isPublished.Value)
                {
                    filter &= Builders<WorkflowModel>.Filter.Eq(w => w.IsPublished, true);
                }
                else
                {
                    filter &= Builders<WorkflowModel>.Filter.Or(
                        Builders<WorkflowModel>.Filter.Eq(w => w.IsPublished, false),
                        Builders<WorkflowModel>.Filter.Exists(w => w.IsPublished, false)
                    );
                }
            }
            return collection.CountDocumentsAsync(filter);
        }

        public Task<List<WorkflowModel>> GetAllWorkflowsAsync(string tenantId, int pageSize, int pageNumber, string? search, bool? isPublished)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowModel>.Filter.Empty;
            if (!string.IsNullOrEmpty(search))
            {
                filter &= Builders<WorkflowModel>.Filter.Regex(w => w.Name, new MongoDB.Bson.BsonRegularExpression(search, "i"));
            }
            if (isPublished.HasValue)
            {
                if (isPublished.Value)
                {
                    filter &= Builders<WorkflowModel>.Filter.Eq(w => w.IsPublished, true);
                }
                else
                {
                    filter &= Builders<WorkflowModel>.Filter.Or(
                        Builders<WorkflowModel>.Filter.Eq(w => w.IsPublished, false),
                        Builders<WorkflowModel>.Filter.Exists(w => w.IsPublished, false)
                    );
                }
            }
            return collection.Find(filter)
                .SortByDescending(x => x.CreatedDate)
                .Skip(pageNumber * pageSize)
                .Limit(pageSize).ToListAsync();
        }

        public async Task<WorkflowModel> GetWorkflowAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowModel>.Filter.Eq(w => w.ItemId, workflowId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<WorkflowModel>> GetWorkflowsByMailServerConfigurationIdAsync(string tenantId, string mailServerConfigurationId)
        {
            var collection = GetCollection(tenantId);

            var filter = Builders<WorkflowModel>.Filter.ElemMatch(w => w.Nodes, Builders<NodeModel>.Filter.Eq("Parameters.mailServerConfigurationId", mailServerConfigurationId));
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<List<WorkflowModel>> GetWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation)
        {
            var collection = GetCollection(tenantId);

            var nodeFilter = Builders<NodeModel>.Filter.And(
                Builders<NodeModel>.Filter.Eq("Parameters.collectionName", collectionName),
                Builders<NodeModel>.Filter.Eq("Parameters.operation", operation),
                Builders<NodeModel>.Filter.Eq("Type", "dataGateway"),
                Builders<NodeModel>.Filter.Eq("Category", "trigger")
            );

            var filter = Builders<WorkflowModel>.Filter.And(
                Builders<WorkflowModel>.Filter.ElemMatch(w => w.Nodes, nodeFilter)
            );

            return await collection.Find(filter).ToListAsync();
        }

        public async Task<List<WorkflowModel>> GetPublishWorkflowsByDataCollectionAsync(string tenantId, string collectionName, string operation)
        {
            var collection = GetCollection(tenantId);

            var nodeFilter = Builders<NodeModel>.Filter.And(
                Builders<NodeModel>.Filter.Eq("Parameters.collectionName", collectionName),
                Builders<NodeModel>.Filter.Eq("Parameters.operation", operation),
                Builders<NodeModel>.Filter.Eq("Type", "dataGateway"),
                Builders<NodeModel>.Filter.Eq("Category", "trigger")
            );

            var filter = Builders<WorkflowModel>.Filter.And(
                Builders<WorkflowModel>.Filter.Eq(w => w.IsPublished, true),
                Builders<WorkflowModel>.Filter.ElemMatch(w => w.PublishedMeta.TriggerNodes, nodeFilter)
            );

            return await collection.Find(filter).ToListAsync();
        }

        public async Task UpdateWorkflowAsync(WorkflowModel workflow)
        {
            if (string.IsNullOrEmpty(workflow.TenantId))
                throw new InvalidOperationException("TenantId is required for workflow");

            var collection = GetCollection(workflow.TenantId);
            var result = await collection.ReplaceOneAsync(item => item.ItemId == workflow.ItemId, workflow);
            if (result.MatchedCount == 0)
            {
                throw new KeyNotFoundException($"Workflow with ItemId '{workflow.ItemId}' not found");
            }
        }

        public async Task DeleteWorkflowAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowModel>.Filter.Eq(w => w.ItemId, workflowId);
            await collection.DeleteOneAsync(filter);
        }

    }
}