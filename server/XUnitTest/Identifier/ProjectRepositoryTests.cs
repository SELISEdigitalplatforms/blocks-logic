using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    /// <summary>
    /// Unit tests for <see cref="ProjectRepository"/>. The tenant database is resolved once in the
    /// constructor from the ambient <see cref="BlocksContext"/>, so the context is installed before the
    /// repository is built. Mongo access is exercised through mocked collections and cursors.
    /// </summary>
    public class ProjectRepositoryTests : IDisposable
    {
        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();
        private readonly Mock<IMongoDatabase> _clientDb = new();

        private readonly Mock<IMongoCollection<Tenant>> _clientTenants = new();
        private readonly Mock<IMongoCollection<Tenant>> _providerTenants = new();
        private readonly Mock<IMongoCollection<Project>> _clientProjects = new();
        private readonly Mock<IMongoCollection<Project>> _providerProjects = new();
        private readonly Mock<IMongoCollection<ProjectPeople>> _clientProjectPeoples = new();
        private readonly Mock<IMongoCollection<ProjectPeople>> _providerProjectPeoples = new();
        private readonly Mock<IMongoCollection<BlocksGuid>> _blocksGuids = new();

        public ProjectRepositoryTests()
        {
            TestBlocksContext.Set();
            _blocksSecret.SetupGet(s => s.DatabaseConnectionString).Returns("mongodb://localhost");

            _dbContextProvider.Setup(p => p.GetDatabase("tenant-123")).Returns(_clientDb.Object);
            _clientDb.Setup(d => d.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName, null)).Returns(_clientTenants.Object);
            _clientDb.Setup(d => d.GetCollection<Project>(IdentifierConstants.TenantCollectionName, null)).Returns(_clientProjects.Object);
            _clientDb.Setup(d => d.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName, null)).Returns(_clientProjectPeoples.Object);
            _clientDb.Setup(d => d.GetCollection<BlocksGuid>("BlocksGuids", null)).Returns(_blocksGuids.Object);

            _dbContextProvider.Setup(p => p.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName)).Returns(_providerTenants.Object);
            _dbContextProvider.Setup(p => p.GetCollection<Project>(IdentifierConstants.TenantCollectionName)).Returns(_providerProjects.Object);
            _dbContextProvider.Setup(p => p.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName)).Returns(_providerProjectPeoples.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ProjectRepository CreateRepository() => new(_dbContextProvider.Object, _blocksSecret.Object);

        private static IAsyncCursor<T> NewCursor<T>(List<T> items)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.Setup(c => c.Current).Returns(items);
            cursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>())).Returns(items.Count > 0).Returns(false);
            cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(items.Count > 0).ReturnsAsync(false);
            return cursor.Object;
        }

        private static void SetupFind<T>(Mock<IMongoCollection<T>> collection, params T[] items)
        {
            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T, T>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor([.. items]));
        }

        private static void SetupProjection<T, TProjection>(Mock<IMongoCollection<T>> collection, params TProjection[] items)
        {
            collection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<T>>(),
                    It.IsAny<FindOptions<T, TProjection>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor([.. items]));
        }

        private static Tenant Tenant(string tenantId = "DTENANT-1", string groupId = "group-1") => new()
        {
            ItemId = "project-item-1",
            TenantId = tenantId,
            TenantGroupId = groupId,
            DBName = "tenantdb",
            Name = "Demo",
            Environment = "dev",
            DbConnectionString = "mongodb://localhost",
            Applications = [new Applications { Domain = "https://demo.test", CookieDomain = "demo.test" }],
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "pw", IssueDate = DateTime.UtcNow }
        };

        #region Tenant reads

        [Fact]
        public void Constructor_ImpersonatedContext_ResolvesTheRootDatabase()
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-123", roles: [], userId: "user-123", isAuthenticated: true, requestUri: string.Empty,
                organizationId: "org-123", expireOn: DateTime.UtcNow.AddHours(1), email: "test@example.com",
                permissions: [], userName: "testuser", phoneNumber: string.Empty, displayName: "Test User",
                oauthToken: string.Empty, originalTenantId: "tenant-123", applicationDomain: string.Empty,
                impersonated: true), true);

            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://localhost", "BlocksRootDb", It.IsAny<bool>()))
                .Returns(_clientDb.Object);

            CreateRepository().Should().NotBeNull();

            _dbContextProvider.Verify(p => p.GetDatabase("mongodb://localhost", "BlocksRootDb", It.IsAny<bool>()), Times.Once);
            _dbContextProvider.Verify(p => p.GetDatabase(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetByTenantIdAsync_NoMatch_ReturnsNull()
        {
            SetupFind(_clientTenants);

            var result = await CreateRepository().GetByTenantIdAsync("DTENANT-9");

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetProjectIdsByGroupId_ProjectsTenantIds()
        {
            SetupProjection<Tenant, string>(_providerTenants, "DTENANT-1", "PTENANT-1");

            var result = await CreateRepository().GetProjectIdsByGroupId("group-1");

            result.Should().BeEquivalentTo(["DTENANT-1", "PTENANT-1"]);
        }

        #endregion

        #region Shared projects

        [Fact]
        public async Task GetProjectPeoplesAsync_ReturnsProjectsForTheGroup()
        {
            SetupFind(_providerProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" });
            SetupFind(_providerProjects, new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1" });

            var result = await CreateRepository().GetProjectPeoplesAsync("group-1");

            result.Should().ContainSingle();
        }

        [Fact]
        public async Task GetAllByLastModifiedDateAsync_GroupsOwnedAndSharedProjects()
        {
            SetupFind(_clientProjects,
                new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1", CreatedBy = "user-123" });
            SetupFind(_clientProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "STENANT-2", UserId = "user-123" });
            SetupFind(_providerProjects, new Project { TenantId = "PTENANT-3", TenantGroupId = "group-2" });

            var result = await CreateRepository().GetAllByLastModifiedDateAsync(new GetProjectsRequest { PageSize = 10 });

            result.Should().HaveCount(2);
            result.Should().Contain(g => !g.IsShared && g.TenantGroupId == "group-1");
            result.Should().Contain(g => g.IsShared);
            result.Single(g => g.IsShared).NonSharedProject.Should().ContainSingle(p => p.TenantId == "PTENANT-3");
        }

        [Fact]
        public async Task GetAllByLastModifiedDateAsync_WithGroupId_NarrowsTheOwnedFilter()
        {
            SetupFind(_clientProjects);
            SetupFind(_clientProjectPeoples);
            SetupFind(_providerProjects);

            var result = await CreateRepository().GetAllByLastModifiedDateAsync(
                new GetProjectsRequest { TenantGroupId = "group-1", PageSize = 10 });

            result.Should().BeEmpty();
        }

        #endregion

        #region Lookups

        [Fact]
        public async Task GetBlocksGuidAsync_ReturnsTheEncodedGuidForTheGroup()
        {
            SetupFind(_blocksGuids, new BlocksGuid { ItemId = "guid-1", TenantGroupId = "group-1", EncodedValue = "abcde" });

            var result = await CreateRepository().GetBlocksGuidAsync("group-1");

            result.EncodedValue.Should().Be("abcde");
        }

        #endregion
    }
}
