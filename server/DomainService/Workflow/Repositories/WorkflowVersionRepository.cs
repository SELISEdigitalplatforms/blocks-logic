using Blocks.Genesis;
using DomainService.Workflow.Entities;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Repositories
{
    [ExcludeFromCodeCoverage]
    public class WorkflowVersionRepository : IWorkflowVersionRepository
    {

        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "WorkflowVersions";

        public WorkflowVersionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }


        private IMongoCollection<WorkflowVersionEntity> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowVersionEntity>(tenantId, _collectionName);
        }

        public async Task CreateWorkflowVersionAsync(WorkflowVersionEntity versionModel)
        {
            var collection = GetCollection(versionModel.TenantId);
            await collection.InsertOneAsync(versionModel, null);
        }

        public Task<List<WorkflowVersionEntity>> GetWorkflowVersionsAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filters = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.Eq(f => f.WorkflowId, workflowId);

            return collection.Find(filters).SortByDescending(f => f.CreatedDate).ToListAsync();
        }

        public Task<List<WorkflowVersionEntity>> GetWorkflowVersionsAsync(string tenantId, string[] workflowIds)
        {
            var collection = GetCollection(tenantId);
            var filters = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.In(f => f.WorkflowId, workflowIds);

            return collection.Find(filters).SortByDescending(f => f.CreatedDate).ToListAsync();
        }

        public Task<WorkflowVersionEntity> GetWorkflowVersionAsync(string tenantId, string versionId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.Eq(f => f.ItemId, versionId);
            return collection.Find(filter).FirstOrDefaultAsync();
        }

        public Task<WorkflowVersionEntity> UpdateWorkflowVersionAsync(string tenantId, string versionId, WorkflowVersionEntity versionModel)
        {
            var collection = GetCollection(tenantId);
            return collection.FindOneAndReplaceAsync(filter => filter.TenantId == tenantId && filter.ItemId == versionId, versionModel);
        }

        public Task DeleteWorkflowVersionAsync(string tenantId, string versionId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.Eq(f => f.ItemId, versionId);
            return collection.DeleteOneAsync(filter);
        }

        public Task DeleteWorkflowVersionsByWorkflowIdAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.Eq(f => f.WorkflowId, workflowId);
            return collection.DeleteManyAsync(filter);
        }
    }
}