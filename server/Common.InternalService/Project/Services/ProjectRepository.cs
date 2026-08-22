using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Dtos;
using DomainService.Shared.Entities;
using MongoDB.Driver;
using DomainService.Shared;

namespace DomainService.Projects
{
    public class ProjectRepository : IProjectRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;
        private readonly IMongoDatabase _clientDb;

        public ProjectRepository(IDbContextProvider dbContextProvider,
                                 IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
            _clientDb = ResolvedClientDb();
        }
        private IMongoDatabase ResolvedClientDb()
        {
            var blocksContext = BlocksContext.GetContext();

            if (blocksContext.Impersonated)
            {
                return _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, "BlocksRootDb");
            }

            return _dbContextProvider.GetDatabase(blocksContext.TenantId);
        }

        public async Task<List<GroupedProjectsDto>> GetAllByLastModifiedDateAsync(GetProjectsRequest request)
        {
            var collection = _clientDb.GetCollection<Project>(IdentifierConstants.TenantCollectionName);

            var filter = !string.IsNullOrEmpty(request.TenantGroupId) ?

                          Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                       Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false),
                                                       Builders<Project>.Filter.Eq(mc => mc.TenantGroupId, request.TenantGroupId)) :

                          Builders<Project>.Filter.And(Builders<Project>.Filter.Eq(mc => mc.CreatedBy, BlocksContext.GetContext()?.UserId),
                                                       Builders<Project>.Filter.Eq(mc => mc.IsDisabled, false));

            var option = new FindOptions<Project>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            };

            using var cursor = await collection.FindAsync(filter, option);
            var selfProjects = await cursor.ToListAsync();
            var selfGroupedProjectsTasks = selfProjects.GroupBy(p => p.TenantGroupId ?? string.Empty)
                                               .Select(g => new GroupedProjectsDto
                                               {
                                                   TenantGroupId = g.Key,
                                                   Projects = g.OrderByDescending(p => p.LastUpdatedBy).ToList(),
                                                   IsShared = false,
                                                   NonSharedProject = []
                                               }).ToList();


            var sharedProjects = await GetSharedProjectsAsync(request.TenantGroupId);

            var sharedGroupProjects = sharedProjects.GroupBy(p => p.TenantGroupId ?? string.Empty)
                                               .Select(async g => new GroupedProjectsDto
                                               {
                                                   TenantGroupId = g.Key,
                                                   Projects = g.OrderByDescending(p => p.LastUpdatedBy).ToList(),
                                                   IsShared = true,
                                                   NonSharedProject = await GetNosharedProjectsAsync(sharedProjects, g.Key)
                                               }).ToList();

            var groupedSharedProject = (await Task.WhenAll(sharedGroupProjects)).ToList();
            return [.. selfGroupedProjectsTasks, .. groupedSharedProject];
        }

        private async Task<List<Project>> GetNosharedProjectsAsync(List<Project> sharedProjects, string tenantGroupId)
        {
            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.Nin(p => p.TenantId, sharedProjects?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false) &
                         Builders<Project>.Filter.Where(p => p.TenantGroupId == tenantGroupId);

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        private async Task<List<Project>> GetSharedProjectsAsync(string? tenantGroupId = null)
        {
            var projectPeopleCollection = _clientDb.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName);

            var projectPeopleFilter = Builders<ProjectPeople>.Filter.And(
                Builders<ProjectPeople>.Filter.Eq(mc => mc.UserId, BlocksContext.GetContext()?.UserId),
                Builders<ProjectPeople>.Filter.Or(
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsInvitationConfirmed, true),
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsCreator, true)));

            var documentsCursor = await projectPeopleCollection.FindAsync(projectPeopleFilter);
            var documents = await documentsCursor.ToListAsync();

            var projectCollection = _clientDb.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.In(p => p.TenantId, documents?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false) &
                         Builders<Project>.Filter.Ne(p => p.CreatedBy, BlocksContext.GetContext().UserId);

            if (!string.IsNullOrEmpty(tenantGroupId))
            {
                filter &= Builders<Project>.Filter.Eq(p => p.TenantGroupId, tenantGroupId);
            }

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task<List<Project>> GetProjectPeoplesAsync(string tenantGroupId)
        {
            var projectPeopleCollection = _dbContextProvider.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName);

            var projectPeopleFilter = Builders<ProjectPeople>.Filter.And(
                Builders<ProjectPeople>.Filter.Eq(mc => mc.UserId, BlocksContext.GetContext()?.UserId),
                Builders<ProjectPeople>.Filter.Or(
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsInvitationConfirmed, true),
                    Builders<ProjectPeople>.Filter.Eq(mc => mc.IsCreator, true)));

            var documentsCursor = await projectPeopleCollection.FindAsync(projectPeopleFilter);
            var documents = await documentsCursor.ToListAsync();

            var projectCollection = _dbContextProvider.GetCollection<Project>(IdentifierConstants.TenantCollectionName);
            var filter = Builders<Project>.Filter.In(p => p.TenantId, documents?.Select(doc => doc?.TenantId)) &
                         Builders<Project>.Filter.Where(p => p.IsDisabled == false);

            filter &= Builders<Project>.Filter.Eq(p => p.TenantGroupId, tenantGroupId);

            using var projectCursor = await projectCollection.FindAsync(filter, new FindOptions<Project>
            {
                Sort = Builders<Project>.Sort.Descending(doc => doc.LastUpdatedBy)
            });

            return await projectCursor.ToListAsync();
        }

        public async Task<Tenant> GetByTenantIdAsync(string tenantId)
        {
            var collection = _clientDb.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName);

            var filter = Builders<Tenant>.Filter.Eq(mc => mc.TenantId, tenantId)
                        & Builders<Tenant>.Filter.Eq(mc => mc.IsDisabled, false);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<BlocksGuid> GetBlocksGuidAsync(string tenantGroupId)
        {
            var collection = _clientDb.GetCollection<BlocksGuid>($"{nameof(BlocksGuid)}s");
            var filter = Builders<BlocksGuid>.Filter.Eq(mc => mc.TenantGroupId, tenantGroupId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<string>> GetProjectIdsByGroupId(string projectGroupId)
        {
            var filter = Builders<Tenant>.Filter.Eq(x => x.TenantGroupId, projectGroupId);

            var tenantIds = await _dbContextProvider.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName)
                .Find(filter)
                .Project(x => x.TenantId)
                .ToListAsync();

            return tenantIds;
        }
    }
}
