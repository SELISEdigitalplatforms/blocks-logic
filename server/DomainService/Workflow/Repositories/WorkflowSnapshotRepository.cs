using Blocks.Genesis;
using DomainService.Workflow.Models;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Repositories
{
    [ExcludeFromCodeCoverage]
    public class WorkflowSnapshotRepository : IWorkflowSnapshotRepository
    {

        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "WorkflowSnapshots";

        public WorkflowSnapshotRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }


        private IMongoCollection<WorkflowSnapshotModel> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowSnapshotModel>(tenantId, _collectionName);
        }

        public async Task CreateWorkflowSnapshotAsync(WorkflowSnapshotModel snapshot)
        {
            var collection = GetCollection(snapshot.TenantId);
            await collection.InsertOneAsync(snapshot, null);
        }

        public Task<List<WorkflowSnapshotModel>> GetWorkflowSnapshotsAsync(string projectKey, string workflowId)
        {
            var collection = GetCollection(projectKey);
            return collection.Find(filter => filter.TenantId == projectKey && filter.WorkflowId == workflowId).ToListAsync();
        }

        public Task<WorkflowSnapshotModel> GetWorkflowSnapshotAsync(string projectKey, string id)
        {
            var collection = GetCollection(projectKey);
            return collection.Find(filter => filter.TenantId == projectKey && filter.ItemId == id).FirstOrDefaultAsync();
        }
    }
}