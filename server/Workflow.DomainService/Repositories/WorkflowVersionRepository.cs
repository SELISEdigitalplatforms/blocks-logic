using Blocks.Genesis;
using DataAccess.InternalService;
using Workflow.DomainService.Entities;
using MongoDB.Driver;
using System.Diagnostics.CodeAnalysis;
using Blocks.Genesis.Entities.Logic;

namespace Workflow.DomainService.Repositories
{
    [ExcludeFromCodeCoverage]
    public class WorkflowVersionRepository : IWorkflowVersionRepository
    {

        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "WorkflowVersions";

        private readonly ILogicDataAccess _dataAccess;

        public WorkflowVersionRepository(IDbContextProvider dbContextProvider, ILogicDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }


        private IMongoCollection<WorkflowVersionEntity> GetCollection(string tenantId)
        {
            return _dbContextProvider.GetCollection<WorkflowVersionEntity>(tenantId, _collectionName);
        }

        private Task<ReadScope<WorkflowVersionEntity>> ReadAsync(string tenantId, FilterDefinition<WorkflowVersionEntity> filter, string callSite, bool useProjection = true) =>
            _dataAccess.ReadAsync(LogicSchemas.WorkflowVersionEntity, filter, $"WorkflowVersionRepository.{callSite}", tenantId, useProjection);

        public async Task CreateWorkflowVersionAsync(WorkflowVersionEntity versionModel)
        {
            if (!await _dataAccess.InsertAllowedAsync(LogicSchemas.WorkflowVersionEntity, "WorkflowVersionRepository.CreateWorkflowVersion", versionModel.TenantId))
                throw new UnauthorizedAccessException("Workflow version create is not allowed by the data access policy.");
            var collection = GetCollection(versionModel.TenantId);
            await collection.InsertOneAsync(versionModel, null);
        }

        public async Task<List<WorkflowVersionEntity>> GetWorkflowVersionsAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filters = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.Eq(f => f.WorkflowId, workflowId);

            var scope = await ReadAsync(tenantId, filters, "GetWorkflowVersions");
            return scope.Granted ? await scope.Apply(collection).SortByDescending(f => f.CreatedDate).ToListAsync() : [];
        }

        public async Task<List<WorkflowVersionEntity>> GetWorkflowVersionsAsync(string tenantId, string[] workflowIds)
        {
            var collection = GetCollection(tenantId);
            var filters = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.In(f => f.WorkflowId, workflowIds);

            var scope = await ReadAsync(tenantId, filters, "GetWorkflowVersionsByIds");
            return scope.Granted ? await scope.Apply(collection).SortByDescending(f => f.CreatedDate).ToListAsync() : [];
        }

        public async Task<WorkflowVersionEntity> GetWorkflowVersionAsync(string tenantId, string versionId)
        {
            var collection = GetCollection(tenantId);
            var filter = Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                         Builders<WorkflowVersionEntity>.Filter.Eq(f => f.ItemId, versionId);
            var scope = await ReadAsync(tenantId, filter, "GetWorkflowVersion");
            return scope.Granted ? await scope.Apply(collection).FirstOrDefaultAsync() : null!;
        }

        public async Task<WorkflowVersionEntity> UpdateWorkflowVersionAsync(string tenantId, string versionId, WorkflowVersionEntity versionModel)
        {
            var collection = GetCollection(tenantId);
            var filter = await _dataAccess.EditFilterAsync(LogicSchemas.WorkflowVersionEntity,
                Builders<WorkflowVersionEntity>.Filter.Where(f => f.TenantId == tenantId && f.ItemId == versionId),
                "WorkflowVersionRepository.UpdateWorkflowVersion", tenantId);
            return filter is null ? null! : await collection.FindOneAndReplaceAsync(filter, versionModel);
        }

        public async Task DeleteWorkflowVersionAsync(string tenantId, string versionId)
        {
            var collection = GetCollection(tenantId);
            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.WorkflowVersionEntity,
                Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                Builders<WorkflowVersionEntity>.Filter.Eq(f => f.ItemId, versionId),
                "WorkflowVersionRepository.DeleteWorkflowVersion", tenantId);
            if (filter is null) return;
            await collection.DeleteOneAsync(filter);
        }

        public async Task DeleteWorkflowVersionsByWorkflowIdAsync(string tenantId, string workflowId)
        {
            var collection = GetCollection(tenantId);
            var filter = await _dataAccess.DeleteFilterAsync(LogicSchemas.WorkflowVersionEntity,
                Builders<WorkflowVersionEntity>.Filter.Eq(f => f.TenantId, tenantId) &
                Builders<WorkflowVersionEntity>.Filter.Eq(f => f.WorkflowId, workflowId),
                "WorkflowVersionRepository.DeleteWorkflowVersionsByWorkflowId", tenantId);
            if (filter is null) return;
            await collection.DeleteManyAsync(filter);
        }
    }
}