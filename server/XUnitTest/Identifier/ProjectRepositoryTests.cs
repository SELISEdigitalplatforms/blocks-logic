using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using DomainService.Shared.Entities;
using DomainService.Shared.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
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
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly Mock<IEncodingService> _encodingService = new();
        private readonly Mock<IMongoDatabase> _clientDb = new();

        private readonly Mock<IMongoCollection<Tenant>> _clientTenants = new();
        private readonly Mock<IMongoCollection<Tenant>> _providerTenants = new();
        private readonly Mock<IMongoCollection<Project>> _clientProjects = new();
        private readonly Mock<IMongoCollection<Project>> _providerProjects = new();
        private readonly Mock<IMongoCollection<ProjectPeople>> _clientProjectPeoples = new();
        private readonly Mock<IMongoCollection<ProjectPeople>> _providerProjectPeoples = new();
        private readonly Mock<IMongoCollection<TenantAsset>> _tenantAssets = new();
        private readonly Mock<IMongoCollection<ProjectStatusTracer>> _statusTracers = new();
        private readonly Mock<IMongoCollection<SsoInfo>> _ssoInfos = new();
        private readonly Mock<IMongoCollection<BlocksGuid>> _blocksGuids = new();
        private readonly Mock<IMongoCollection<ThirdPartyJWTClaims>> _jwtClaims = new();

        public ProjectRepositoryTests()
        {
            TestBlocksContext.Set();
            _blocksSecret.SetupGet(s => s.DatabaseConnectionString).Returns("mongodb://localhost");
            _configuration.Setup(c => c["KbtclIdentifier"]).Returns(".blocks.test");
            _encodingService.Setup(e => e.EncodeToBase26Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("abcde");

            _dbContextProvider.Setup(p => p.GetDatabase("tenant-123")).Returns(_clientDb.Object);
            _clientDb.Setup(d => d.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName, null)).Returns(_clientTenants.Object);
            _clientDb.Setup(d => d.GetCollection<Project>(IdentifierConstants.TenantCollectionName, null)).Returns(_clientProjects.Object);
            _clientDb.Setup(d => d.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName, null)).Returns(_clientProjectPeoples.Object);
            _clientDb.Setup(d => d.GetCollection<BlocksGuid>("BlocksGuids", null)).Returns(_blocksGuids.Object);

            _dbContextProvider.Setup(p => p.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName)).Returns(_providerTenants.Object);
            _dbContextProvider.Setup(p => p.GetCollection<Project>(IdentifierConstants.TenantCollectionName)).Returns(_providerProjects.Object);
            _dbContextProvider.Setup(p => p.GetCollection<ProjectPeople>(IdentifierConstants.ProjectPeopleCollectionName)).Returns(_providerProjectPeoples.Object);
            _dbContextProvider.Setup(p => p.GetCollection<ProjectPeople>("ProjectPeoples")).Returns(_providerProjectPeoples.Object);
            _dbContextProvider.Setup(p => p.GetCollection<TenantAsset>(IdentifierConstants.TenantAssetCollectionName)).Returns(_tenantAssets.Object);
            _dbContextProvider.Setup(p => p.GetCollection<ProjectStatusTracer>(IdentifierConstants.ProjectStatusTracerCollectionName)).Returns(_statusTracers.Object);
            _dbContextProvider.Setup(p => p.GetCollection<SsoInfo>("SocialLoginCredentials")).Returns(_ssoInfos.Object);
            _dbContextProvider.Setup(p => p.GetCollection<ThirdPartyJWTClaims>("ThirdPartyJWTClaims")).Returns(_jwtClaims.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ProjectRepository CreateRepository() => new(
            _dbContextProvider.Object, _configuration.Object, _blocksSecret.Object, _encodingService.Object);

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

        #region Tenant reads and writes

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
        public async Task GetByIdAsync_ReturnsTheEnabledTenant()
        {
            SetupFind(_clientTenants, Tenant());

            var result = await CreateRepository().GetByIdAsync("project-item-1");

            result.Should().NotBeNull();
            result.TenantId.Should().Be("DTENANT-1");
        }

        [Fact]
        public async Task GetByTenantIdAsync_NoMatch_ReturnsNull()
        {
            SetupFind(_clientTenants);

            var result = await CreateRepository().GetByTenantIdAsync("DTENANT-9");

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetByGroupIdAsync_ReturnsEveryTenantInTheGroup()
        {
            SetupFind(_providerTenants, Tenant("DTENANT-1"), Tenant("PTENANT-1"));

            var result = await CreateRepository().GetByGroupIdAsync("group-1");

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetByDomainAsync_MatchesOnApplicationDomain()
        {
            SetupFind(_providerTenants, Tenant());

            var result = await CreateRepository().GetByDomainAsync("https://demo.test");

            result.Should().NotBeNull();
        }

        [Fact]
        public async Task InsertProjectAsync_InsertsIntoTheTenantsCollection()
        {
            var project = Tenant();

            await CreateRepository().InsertProjectAsync(project);

            _providerTenants.Verify(c => c.InsertOneAsync(project, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateProjectAsync_ReplacesTheStoredTenant()
        {
            var project = Tenant();

            await CreateRepository().UpdateProjectAsync(project);

            _providerTenants.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<Tenant>>(), project, It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetProjectCountAsync_ReturnsTheCountForTheCurrentUser()
        {
            _providerProjects.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Project>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(3);

            var result = await CreateRepository().GetProjectCountAsync();

            result.Should().Be(3);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(2, true)]
        public async Task IsExistingEnviroment_ReflectsTheDocumentCount(long count, bool expected)
        {
            _providerProjects.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<Project>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(count);

            var result = await CreateRepository().IsExistingEnviroment(["dev"], "group-1");

            result.Should().Be(expected);
        }

        [Fact]
        public async Task GetProjectIdsByGroupId_ProjectsTenantIds()
        {
            SetupProjection<Tenant, string>(_providerTenants, "DTENANT-1", "PTENANT-1");

            var result = await CreateRepository().GetProjectIdsByGroupId("group-1");

            result.Should().BeEquivalentTo(["DTENANT-1", "PTENANT-1"]);
        }

        [Fact]
        public async Task UpdateTenantGroupAsync_RenamesEveryTenantInTheGroup()
        {
            SetupProjection<Tenant, string>(_providerTenants, "DTENANT-1");

            await CreateRepository().UpdateTenantGroupAsync(new UpdateTenantGroupRequest { TenantGroupId = "group-1", Name = "Renamed" });

            _providerTenants.Verify(c => c.UpdateManyAsync(
                It.IsAny<FilterDefinition<Tenant>>(), It.IsAny<UpdateDefinition<Tenant>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region Assets

        [Fact]
        public async Task UpdateTenantAssetAsync_InsertsTheAsset()
        {
            var asset = new TenantAsset { ItemId = "asset-1", TenantGroupId = "group-1", Resources = [] };

            await CreateRepository().UpdateTenantAssetAsync(asset);

            _tenantAssets.Verify(c => c.InsertOneAsync(asset, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveTenantAssetAsync_UpsertsTheAsset()
        {
            var asset = new TenantAsset { ItemId = "asset-1", TenantGroupId = "group-1", Resources = [] };

            await CreateRepository().SaveTenantAssetAsync(asset);

            _tenantAssets.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<TenantAsset>>(), asset,
                It.Is<ReplaceOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetTenantAssetAsync_NoSharedProjects_ReturnsNothing()
        {
            SetupFind(_providerProjectPeoples);
            SetupFind(_providerProjects);

            var (assets, totalCount) = await CreateRepository().GetTenantAssetAsync(new GetAssetRequest { TenantGroupId = "group-1" });

            assets.Should().BeNull();
            totalCount.Should().Be(0);
        }

        [Fact]
        public async Task GetTenantAssetAsync_NoStoredAsset_ReturnsNothing()
        {
            SetupFind(_providerProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" });
            SetupFind(_providerProjects, new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1" });
            SetupFind(_tenantAssets);

            var (assets, totalCount) = await CreateRepository().GetTenantAssetAsync(new GetAssetRequest { TenantGroupId = "group-1" });

            assets.Should().BeNull();
            totalCount.Should().Be(0);
        }

        [Fact]
        public async Task GetTenantAssetAsync_FiltersByNameAndPagesTheResources()
        {
            SetupFind(_providerProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" });
            SetupFind(_providerProjects, new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1" });
            SetupFind(_tenantAssets, new TenantAsset
            {
                ItemId = "asset-1",
                TenantGroupId = "group-1",
                Resources =
                [
                    new Resource { ResourceId = "r1", Name = "web-app", Link = "https://git.test/web" },
                    new Resource { ResourceId = "r2", Name = "web-api", Link = "https://git.test/api" },
                    new Resource { ResourceId = "r3", Name = "mobile", Link = "https://git.test/mobile" }
                ]
            });

            var (assets, totalCount) = await CreateRepository().GetTenantAssetAsync(new GetAssetRequest
            {
                TenantGroupId = "group-1",
                Page = 0,
                PageSize = 1,
                Filter = new GetAssetFilter { Name = " WEB " }
            });

            totalCount.Should().Be(2);
            assets!.Resources.Should().ContainSingle(r => r.ResourceId == "r1");
        }

        [Fact]
        public async Task GetTenantAssetAsync_FiltersByLink()
        {
            SetupFind(_providerProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" });
            SetupFind(_providerProjects, new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1" });
            SetupFind(_tenantAssets, new TenantAsset
            {
                ItemId = "asset-1",
                TenantGroupId = "group-1",
                Resources =
                [
                    new Resource { ResourceId = "r1", Name = "web-app", Link = "https://git.test/web" },
                    new Resource { ResourceId = "r2", Name = "mobile", Link = "https://git.test/mobile" }
                ]
            });

            var (assets, totalCount) = await CreateRepository().GetTenantAssetAsync(new GetAssetRequest
            {
                TenantGroupId = "group-1",
                PageSize = 10,
                Filter = new GetAssetFilter { Link = "mobile" }
            });

            totalCount.Should().Be(1);
            assets!.Resources.Should().ContainSingle(r => r.ResourceId == "r2");
        }

        [Fact]
        public async Task GetTenantAssetAsync_AssetWithoutResources_ReturnsEmptyPage()
        {
            SetupFind(_providerProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" });
            SetupFind(_providerProjects, new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1" });
            SetupFind(_tenantAssets, new TenantAsset { ItemId = "asset-1", TenantGroupId = "group-1", Resources = null! });

            var (assets, totalCount) = await CreateRepository().GetTenantAssetAsync(new GetAssetRequest
            {
                TenantGroupId = "group-1",
                PageSize = 10
            });

            totalCount.Should().Be(0);
            assets!.Resources.Should().BeEmpty();
        }

        #endregion

        #region Shared projects

        [Fact]
        public async Task GetSharedProjectsAsync_WithoutGroupId_ReturnsProjectsSharedWithTheUser()
        {
            SetupFind(_clientProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" });
            SetupFind(_clientProjects, new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1" });

            var result = await CreateRepository().GetSharedProjectsAsync();

            result.Should().ContainSingle(p => p.TenantId == "DTENANT-1");
        }

        [Fact]
        public async Task GetSharedProjectsAsync_WithGroupId_NarrowsTheFilter()
        {
            SetupFind(_clientProjectPeoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" });
            SetupFind(_clientProjects, new Project { TenantId = "DTENANT-1", TenantGroupId = "group-1" });

            var result = await CreateRepository().GetSharedProjectsAsync("group-1");

            result.Should().HaveCount(1);
        }

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

        #region Status tracers, people and lookups

        [Fact]
        public async Task SaveStatusTracerAsync_UpsertsTheTracer()
        {
            var tracer = new ProjectStatusTracer { ProjectId = "project-item-1" };

            await CreateRepository().SaveStatusTracerAsync(tracer);

            _statusTracers.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<ProjectStatusTracer>>(), tracer,
                It.Is<ReplaceOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetAllUnfinishedProjectAsync_ReturnsTracersThatNeverCompleted()
        {
            SetupFind(_statusTracers, new ProjectStatusTracer { ProjectId = "project-item-1" });

            var result = await CreateRepository().GetAllUnfinishedProjectAsync();

            result.Should().ContainSingle();
        }

        [Fact]
        public async Task InsertPeopleAsync_InsertsIntoTheProjectPeoplesCollection()
        {
            var people = new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-123" };

            await CreateRepository().InsertPeopleAsync(people);

            _providerProjectPeoples.Verify(c => c.InsertOneAsync(
                people, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetSsoInfoAsync_ReturnsEnabledProviders()
        {
            SetupFind(_ssoInfos, new SsoInfo(), new SsoInfo());

            var result = await CreateRepository().GetSsoInfoAsync();

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetBlocksGuidAsync_ReturnsTheEncodedGuidForTheGroup()
        {
            SetupFind(_blocksGuids, new BlocksGuid { ItemId = "guid-1", TenantGroupId = "group-1", EncodedValue = "abcde" });

            var result = await CreateRepository().GetBlocksGuidAsync("group-1");

            result.EncodedValue.Should().Be("abcde");
        }

        [Fact]
        public async Task SaveJWTClaimsAsync_UpsertsTheMapper()
        {
            var claims = new ThirdPartyJWTClaims { ItemId = "claims-1" };

            var result = await CreateRepository().SaveJWTClaimsAsync(claims);

            result.IsSuccess.Should().BeTrue();
            _jwtClaims.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<ThirdPartyJWTClaims>>(), claims,
                It.Is<ReplaceOptions>(o => o.IsUpsert), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData("claims-1")]
        [InlineData("")]
        public async Task GetThirdPartyJWTClaimsAsync_ReturnsTheStoredMapper(string itemId)
        {
            SetupFind(_jwtClaims, new ThirdPartyJWTClaims { ItemId = "claims-1" });

            var result = await CreateRepository().GetThirdPartyJWTClaimsAsync(itemId);

            result.ItemId.Should().Be("claims-1");
        }

        #endregion

        #region Default configuration and repo documents

        [Fact]
        public async Task CreateDefaultConfigurationAsync_AlreadyCopied_DoesNothing()
        {
            var tracer = new ProjectStatusTracer { ProjectId = "project-item-1", IsDefaultConfigurationCopied = true };

            await CreateRepository().CreateDefaultConfigurationAsync(tracer, Tenant());

            _dbContextProvider.Verify(p => p.GetDatabase(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task CreateDefaultConfigurationAsync_CopiesSeedDocumentsIntoTheConsumerDatabase()
        {
            var sourceDb = new Mock<IMongoDatabase>();
            var targetDb = new Mock<IMongoDatabase>();
            var sourceCollection = new Mock<IMongoCollection<BsonDocument>>();
            var targetCollection = new Mock<IMongoCollection<BsonDocument>>();

            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://localhost", "BlocksConfiguration", It.IsAny<bool>())).Returns(sourceDb.Object);
            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://localhost", "tenantdb", It.IsAny<bool>())).Returns(targetDb.Object);
            sourceDb.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null)).Returns(sourceCollection.Object);
            targetDb.Setup(d => d.GetCollection<BsonDocument>(It.IsAny<string>(), null)).Returns(targetCollection.Object);

            sourceCollection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor([new BsonDocument { ["_id"] = "seed-1" }]));

            var tracer = new ProjectStatusTracer { ProjectId = "project-item-1" };

            await CreateRepository().CreateDefaultConfigurationAsync(tracer, Tenant());

            tracer.IsDefaultConfigurationCopied.Should().BeTrue();
            targetCollection.Verify(c => c.InsertOneAsync(
                It.Is<BsonDocument>(d => d["CreatedBy"] == "user-123"),
                It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task UpdateIamConfiguration_ExistingDocument_UpdatesTheUrls()
        {
            var targetDb = new Mock<IMongoDatabase>();
            var iamCollection = new Mock<IMongoCollection<BsonDocument>>();
            _dbContextProvider.Setup(p => p.GetDatabase("DTENANT-1")).Returns(targetDb.Object);
            targetDb.Setup(d => d.GetCollection<BsonDocument>("IamConfigurations", null)).Returns(iamCollection.Object);
            iamCollection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor([new BsonDocument { ["_id"] = "iam-1" }]));

            await CreateRepository().UpdateIamConfiguration(Tenant());

            iamCollection.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateIamConfiguration_NoDocument_SkipsTheUpdate()
        {
            var targetDb = new Mock<IMongoDatabase>();
            var iamCollection = new Mock<IMongoCollection<BsonDocument>>();
            _dbContextProvider.Setup(p => p.GetDatabase("DTENANT-1")).Returns(targetDb.Object);
            targetDb.Setup(d => d.GetCollection<BsonDocument>("IamConfigurations", null)).Returns(iamCollection.Object);
            iamCollection.Setup(c => c.FindAsync(
                    It.IsAny<FilterDefinition<BsonDocument>>(),
                    It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewCursor(new List<BsonDocument>()));

            await CreateRepository().UpdateIamConfiguration(Tenant());

            iamCollection.Verify(c => c.UpdateOneAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(), It.IsAny<UpdateDefinition<BsonDocument>>(),
                It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SaveRepoInfoAsync_WritesOneRepoDocumentPerResource()
        {
            var targetDb = new Mock<IMongoDatabase>();
            var reposCollection = new Mock<IMongoCollection<BsonDocument>>();
            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://localhost", "tenantdb", It.IsAny<bool>())).Returns(targetDb.Object);
            targetDb.Setup(d => d.GetCollection<BsonDocument>("Repos", null)).Returns(reposCollection.Object);

            List<BsonDocument>? inserted = null;
            reposCollection.Setup(c => c.InsertManyAsync(
                    It.IsAny<IEnumerable<BsonDocument>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<BsonDocument>, InsertManyOptions, CancellationToken>((docs, _, _) => inserted = [.. docs])
                .Returns(Task.CompletedTask);

            await CreateRepository().SaveRepoInfoAsync(Tenant(),
            [
                new Resource { ResourceId = "r1", Name = "web", Link = "https://git.test/web" },
                new Resource { ResourceId = "r2", Name = "api", Link = "https://git.test/api" }
            ]);

            inserted.Should().HaveCount(2);
            inserted![0]["DefaultDeploymentUrl"].AsString.Should().Be("https://demo.test");
            inserted[0]["Branch"].AsString.Should().Be("dev");
            inserted[1]["DefaultDeploymentUrl"].AsString.Should().Be("https://dabcde-abcde.blocks.test");
        }

        [Fact]
        public async Task SaveRepoInfoAsync_NoResources_WritesNothing()
        {
            await CreateRepository().SaveRepoInfoAsync(Tenant(), []);

            _dbContextProvider.Verify(p => p.GetDatabase("mongodb://localhost", "tenantdb", It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task SaveRepoInfoAsync_ProductionTenant_UsesTheMainBranch()
        {
            var targetDb = new Mock<IMongoDatabase>();
            var reposCollection = new Mock<IMongoCollection<BsonDocument>>();
            _dbContextProvider.Setup(p => p.GetDatabase("mongodb://localhost", "tenantdb", It.IsAny<bool>())).Returns(targetDb.Object);
            targetDb.Setup(d => d.GetCollection<BsonDocument>("Repos", null)).Returns(reposCollection.Object);

            List<BsonDocument>? inserted = null;
            reposCollection.Setup(c => c.InsertManyAsync(
                    It.IsAny<IEnumerable<BsonDocument>>(), It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<BsonDocument>, InsertManyOptions, CancellationToken>((docs, _, _) => inserted = [.. docs])
                .Returns(Task.CompletedTask);

            var project = Tenant();
            project.Environment = "prod";

            await CreateRepository().SaveRepoInfoAsync(project, [new Resource { ResourceId = "r1", Name = "web", Link = "https://git.test/web" }]);

            inserted![0]["Branch"].AsString.Should().Be("main");
        }

        [Fact]
        public async Task UpdateRepoResourceAsync_WritesTheResourceIntoEveryTenantInTheGroup()
        {
            SetupFind(_providerTenants, Tenant("DTENANT-1"), Tenant("PTENANT-1"));

            var targetDb = new Mock<IMongoDatabase>();
            var reposCollection = new Mock<IMongoCollection<BsonDocument>>();
            _dbContextProvider.Setup(p => p.GetDatabase(It.IsAny<string>())).Returns(targetDb.Object);
            targetDb.Setup(d => d.GetCollection<BsonDocument>("Repos", null)).Returns(reposCollection.Object);

            await CreateRepository().UpdateRepoResourceAsync(new AddAssetRequest
            {
                TenantGroupId = "group-1",
                Resource = new Resource { ResourceId = "r1", Name = "web", Link = "https://git.test/web" }
            });

            reposCollection.Verify(c => c.InsertOneAsync(
                It.IsAny<BsonDocument>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        #endregion
    }
}
