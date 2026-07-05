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

        public Task<List<WorkflowVersionModel>> GetWorkflowVersionsAsync(string projectKey, string[] workflowIds)
        {
            var collection = GetCollection(projectKey);
            var filters = Builders<WorkflowVersionModel>.Filter.Eq(f => f.TenantId, projectKey) &
                         Builders<WorkflowVersionModel>.Filter.In(f => f.WorkflowId, workflowIds);

            return collection.Find(filters).SortByDescending(f => f.CreatedDate).ToListAsync();
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

    }
}