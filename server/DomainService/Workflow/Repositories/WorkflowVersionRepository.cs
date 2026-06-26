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

        public Task<List<WorkflowVersionModel>> GetWorkflowVersionsAsync(string projectKey, string workflowId)
        {
            var collection = GetCollection(projectKey);
            return collection.Find(filter => filter.TenantId == projectKey && filter.WorkflowId == workflowId).ToListAsync();
        }

        public Task<WorkflowVersionModel> GetWorkflowVersionAsync(string projectKey, string id)
        {
            var collection = GetCollection(projectKey);
            return collection.Find(filter => filter.TenantId == projectKey && filter.ItemId == id).FirstOrDefaultAsync();
        }
    }
}