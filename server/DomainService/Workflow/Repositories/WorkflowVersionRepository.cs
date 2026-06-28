using Blocks.Genesis;
using DomainService.Workflow.Models;
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


        private IMongoCollection<WorkflowVersionModel> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowVersionModel>(tenantId, _collectionName);
        }

        public async Task CreateWorkflowVersionAsync(WorkflowVersionModel versionModel)
        {
            var collection = GetCollection(versionModel.TenantId);
            await collection.InsertOneAsync(versionModel, null);
        }

        public Task<List<WorkflowVersionModel>> GetWorkflowVersionsAsync(string projectKey, string workflowId, WorkflowVersionFilter? query = null)
        {
            var collection = GetCollection(projectKey);
            var filters = Builders<WorkflowVersionModel>.Filter.Eq(f => f.TenantId, projectKey) &
                         Builders<WorkflowVersionModel>.Filter.Eq(f => f.WorkflowId, workflowId);

            if (query != null && query.IsPublished.HasValue) filters &= Builders<WorkflowVersionModel>.Filter.Eq(f => f.IsPublished, query.IsPublished.Value);

            return collection.Find(filters).ToListAsync();
        }

        public Task<WorkflowVersionModel> GetWorkflowVersionAsync(string projectKey, string versionId)
        {
            var collection = GetCollection(projectKey);
            var filter = Builders<WorkflowVersionModel>.Filter.Eq(f => f.TenantId, projectKey) &
                         Builders<WorkflowVersionModel>.Filter.Eq(f => f.ItemId, versionId);
            return collection.Find(filter).FirstOrDefaultAsync();
        }

        public Task<WorkflowVersionModel> UpdateWorkflowVersionAsync(string projectKey, string versionId, WorkflowVersionModel versionModel)
        {
            var collection = GetCollection(projectKey);
            return collection.FindOneAndReplaceAsync(filter => filter.TenantId == projectKey && filter.ItemId == versionId, versionModel);
        }

        public Task<WorkflowVersionModel> GetPublishedWorkflowVersionAsync(string projectKey, string workflowId)
        {
            var collection = GetCollection(projectKey);
            var filter = Builders<WorkflowVersionModel>.Filter.Eq(f => f.TenantId, projectKey) &
                         Builders<WorkflowVersionModel>.Filter.Eq(f => f.WorkflowId, workflowId) &
                         Builders<WorkflowVersionModel>.Filter.Eq(f => f.IsPublished, true);
            return collection.Find(filter).FirstOrDefaultAsync();
        }
    }
}