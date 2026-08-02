using Blocks.Genesis;
using DomainService.Entities;
using DomainService.People;
using DomainService.Projects;
using DomainService.Shared;
using DomainService.Shared.Entities;
using FluentAssertions;
using Iam.DomainService.Entities;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class PeopleRepositoryTests : IDisposable
    {
        private const string UserCollectionName = "Users";
        private const string PeopleCollectionName = "ProjectPeoples";

        private readonly Mock<IDbContextProvider> _dbContextProvider = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IProjectRepository> _projectRepository = new();

        private readonly Mock<IMongoCollection<ProjectPeople>> _peoples = new();
        private readonly Mock<IMongoCollection<User>> _users = new();
        private readonly Mock<IMongoCollection<Tenant>> _tenantsCollection = new();
        private readonly Mock<IMongoCollection<SignUpSetting>> _signUpSettings = new();

        public PeopleRepositoryTests()
        {
            TestBlocksContext.Set();
            _dbContextProvider.Setup(p => p.GetCollection<ProjectPeople>(PeopleCollectionName)).Returns(_peoples.Object);
            _dbContextProvider.Setup(p => p.GetCollection<User>(UserCollectionName)).Returns(_users.Object);
            _dbContextProvider.Setup(p => p.GetCollection<Tenant>(IdentifierConstants.TenantCollectionName)).Returns(_tenantsCollection.Object);
            _dbContextProvider.Setup(p => p.GetCollection<SignUpSetting>("SignUpSettings")).Returns(_signUpSettings.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private PeopleRepository CreateRepository() => new(_dbContextProvider.Object, _tenants.Object, _projectRepository.Object);

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

        private void SetupPeopleCounts(long totalCount, params string[] distinctUserIds)
        {
            _peoples.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<ProjectPeople>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(totalCount);
            _peoples.Setup(c => c.Distinct(
                    It.IsAny<FieldDefinition<ProjectPeople, string>>(),
                    It.IsAny<FilterDefinition<ProjectPeople>>(),
                    It.IsAny<DistinctOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => NewCursor([.. distinctUserIds]));
        }

        private static Tenant Tenant(string tenantId = "DTENANT-1", string environment = "dev") => new()
        {
            ItemId = "project-item-1",
            TenantId = tenantId,
            TenantGroupId = "group-1",
            Environment = environment,
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "pw", IssueDate = DateTime.UtcNow }
        };

        #region GetPeoplesAsync

        [Fact]
        public async Task GetPeoplesAsync_MergesUserDetailsAndEnvironment()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["DTENANT-1"]);
            _tenants.Setup(t => t.GetTenantByID("DTENANT-1")).Returns(Tenant());
            SetupFind(_peoples, new ProjectPeople
            {
                ItemId = "pp-1",
                TenantId = "DTENANT-1",
                UserId = "user-1",
                IsInvitationSent = true,
                IsInvitationConfirmed = true
            });
            SetupFind(_users, new User
            {
                ItemId = "user-1",
                Email = "invitee@example.com",
                FirstName = "Ada",
                LastName = "Lovelace",
                Salutation = "Ms",
                ProfileImageUrl = "https://cdn.test/ada.png",
                Active = true,
                IsVarified = true
            });
            SetupPeopleCounts(1, "user-1");

            var (peoples, totalCount, peoplesTotalCount, isOwner) =
                await CreateRepository().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1", PageSize = 10 });

            totalCount.Should().Be(1);
            peoplesTotalCount.Should().Be(1);
            isOwner.Should().BeTrue();
            peoples.Should().ContainSingle();
            peoples[0].Enviroment.Should().Be("dev");
            peoples[0].peopleDetails.Email.Should().Be("invitee@example.com");
            peoples[0].peopleDetails.FirstName.Should().Be("Ada");
            peoples[0].peopleDetails.AllowResendActivation.Should().BeFalse();
        }

        [Fact]
        public async Task GetPeoplesAsync_UnknownUserAndTenant_LeavesDetailsBlank()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["DTENANT-1"]);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant)null!);
            SetupFind(_peoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "ghost" });
            SetupFind(_users);
            SetupPeopleCounts(1, "ghost");

            var (peoples, _, _, _) =
                await CreateRepository().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1", PageSize = 10 });

            peoples[0].Enviroment.Should().BeEmpty();
            peoples[0].peopleDetails.Email.Should().BeNull();
        }

        [Fact]
        public async Task GetPeoplesAsync_InactiveUser_AllowsResendingActivation()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["DTENANT-1"]);
            _tenants.Setup(t => t.GetTenantByID("DTENANT-1")).Returns(Tenant());
            SetupFind(_peoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-1" });
            SetupFind(_users, new User { ItemId = "user-1", Email = "invitee@example.com", Active = false, IsVarified = false });
            SetupPeopleCounts(1, "user-1");

            var (peoples, _, _, _) =
                await CreateRepository().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1", PageSize = 10 });

            peoples[0].peopleDetails.AllowResendActivation.Should().BeTrue();
        }

        [Fact]
        public async Task GetPeoplesAsync_WithEnvironmentAndConfirmationFilters_StillQueries()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["DTENANT-1", "PTENANT-1"]);
            _tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(Tenant());
            SetupFind(_peoples);
            SetupFind(_users);
            SetupPeopleCounts(0);

            var (peoples, totalCount, _, _) = await CreateRepository().GetPeoplesAsync(new GetPeoplesRequest
            {
                ProjectGroupId = "group-1",
                EnvironmentIds = ["DTENANT-1"],
                IsInvitationConfirmed = true,
                PageSize = 10
            });

            peoples.Should().BeEmpty();
            totalCount.Should().Be(0);
        }

        [Fact]
        public async Task GetPeoplesAsync_WithSearchFilter_NarrowsByMatchingUserIds()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["DTENANT-1"]);
            _tenants.Setup(t => t.GetTenantByID("DTENANT-1")).Returns(Tenant());
            SetupProjection<User, string>(_users, "user-1");
            SetupFind(_users, new User { ItemId = "user-1", Email = "ada@example.com" });
            SetupFind(_peoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-1" });
            SetupPeopleCounts(1, "user-1");

            var (peoples, _, _, _) = await CreateRepository().GetPeoplesAsync(new GetPeoplesRequest
            {
                ProjectGroupId = "group-1",
                Filter = "ada",
                PageSize = 10
            });

            peoples.Should().ContainSingle();
        }

        #endregion

        #region Simple reads

        [Fact]
        public async Task GetProjectByIdAsync_ReturnsTheTenant()
        {
            SetupFind(_tenantsCollection, Tenant());

            var result = await CreateRepository().GetProjectByIdAsync("DTENANT-1");

            result.TenantId.Should().Be("DTENANT-1");
        }

        [Fact]
        public async Task GetUsersByEmailAsync_ReturnsMatchingUsers()
        {
            SetupFind(_users, new User { ItemId = "user-1", Email = "a@b.com" });

            var result = await CreateRepository().GetUsersByEmailAsync(["a@b.com"]);

            result.Should().ContainSingle();
        }

        [Fact]
        public async Task GetUserByIdAsync_NoMatch_ReturnsNull()
        {
            SetupFind(_users);

            var result = await CreateRepository().GetUserByIdAsync("ghost");

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetProjectPeoplesAsync_ReturnsRecordsForTheUser()
        {
            SetupFind(_peoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-1" });

            var result = await CreateRepository().GetProjectPeoplesAsync("user-1", ["DTENANT-1"]);

            result.Should().ContainSingle();
        }

        [Fact]
        public async Task GetProjectPeopleAsync_ReturnsTheRecord()
        {
            SetupFind(_peoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-1" });

            var result = await CreateRepository().GetProjectPeopleAsync("pp-1");

            result.ItemId.Should().Be("pp-1");
        }

        [Fact]
        public async Task GetProjectPeopleByTenantIdAndUserIdAsync_ReturnsTheRecord()
        {
            SetupFind(_peoples, new ProjectPeople { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-1" });

            var result = await CreateRepository().GetProjectPeopleByTenantIdAndUserIdAsync("DTENANT-1", "user-1");

            result.UserId.Should().Be("user-1");
        }

        [Fact]
        public async Task GetSignUpSettingAsync_ReturnsTheStoredSetting()
        {
            SetupFind(_signUpSettings, new SignUpSetting { IsEmailPasswordSignUpEnabled = true });

            var result = await CreateRepository().GetSignUpSettingAsync();

            result.IsEmailPasswordSignUpEnabled.Should().BeTrue();
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        public async Task IsOwner_ReflectsTheDocumentCount(long count, bool expected)
        {
            _peoples.Setup(c => c.CountDocumentsAsync(
                    It.IsAny<FilterDefinition<ProjectPeople>>(), It.IsAny<CountOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(count);

            var result = await CreateRepository().IsOwner("user-1", ["DTENANT-1"]);

            result.Should().Be(expected);
        }

        #endregion

        #region Resource limits

        [Fact]
        public async Task IsPeoplesWithinLimit_LimitCoversTheInvitees_ReturnsTrue()
        {
            var tenantDb = new Mock<IMongoDatabase>();
            var limits = new Mock<IMongoCollection<ResourceLimit>>();
            _dbContextProvider.Setup(p => p.GetDatabase("DTENANT-1")).Returns(tenantDb.Object);
            tenantDb.Setup(d => d.GetCollection<ResourceLimit>("ResourceLimits", null)).Returns(limits.Object);
            SetupFind(limits, new ResourceLimit { Resource = "people::invite", Limit = 5 });

            var result = await CreateRepository().IsPeoplesWithinLimit(
                new InvitationDetails { ProjectKey = "DTENANT-1", Emails = ["a@b.com", "c@d.com"] }, "people::invite");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task IsPeoplesWithinLimit_LimitTooSmall_ReturnsFalse()
        {
            var tenantDb = new Mock<IMongoDatabase>();
            var limits = new Mock<IMongoCollection<ResourceLimit>>();
            _dbContextProvider.Setup(p => p.GetDatabase("DTENANT-1")).Returns(tenantDb.Object);
            tenantDb.Setup(d => d.GetCollection<ResourceLimit>("ResourceLimits", null)).Returns(limits.Object);
            SetupFind(limits, new ResourceLimit { Resource = "people::invite", Limit = 1 });

            var result = await CreateRepository().IsPeoplesWithinLimit(
                new InvitationDetails { ProjectKey = "DTENANT-1", Emails = ["a@b.com", "c@d.com"] }, "people::invite");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task IsPeoplesWithinLimit_NoLimitConfigured_ReturnsFalse()
        {
            var tenantDb = new Mock<IMongoDatabase>();
            var limits = new Mock<IMongoCollection<ResourceLimit>>();
            _dbContextProvider.Setup(p => p.GetDatabase("DTENANT-1")).Returns(tenantDb.Object);
            tenantDb.Setup(d => d.GetCollection<ResourceLimit>("ResourceLimits", null)).Returns(limits.Object);
            SetupFind(limits);

            var result = await CreateRepository().IsPeoplesWithinLimit(
                new InvitationDetails { ProjectKey = "DTENANT-1", Emails = ["a@b.com"] }, "people::invite");

            result.Should().BeFalse();
        }

        #endregion

        #region Writes

        [Fact]
        public async Task InsertPeoplesAsync_InsertsEveryRecord()
        {
            var records = new List<ProjectPeople> { new() { ItemId = "pp-1", TenantId = "DTENANT-1", UserId = "user-1" } };

            var result = await CreateRepository().InsertPeoplesAsync(records);

            result.Should().BeTrue();
            _peoples.Verify(c => c.InsertManyAsync(records, It.IsAny<InsertManyOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RemovePeoplesAsync_ReturnsTheAcknowledgement()
        {
            _peoples.Setup(c => c.DeleteManyAsync(
                    It.IsAny<FilterDefinition<ProjectPeople>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteResult.Acknowledged(2));

            var result = await CreateRepository().RemovePeoplesAsync("a@b.com", ["DTENANT-1"]);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateProjectPeoples_MarksInvitationsConfirmed()
        {
            _peoples.Setup(c => c.UpdateManyAsync(
                    It.IsAny<FilterDefinition<ProjectPeople>>(), It.IsAny<UpdateDefinition<ProjectPeople>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            var result = await CreateRepository().UpdateProjectPeoples(["pp-1"]);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateProjectPeopleOwnerShipAsync_UpdatesTheCreatorFlag()
        {
            _peoples.Setup(c => c.UpdateManyAsync(
                    It.IsAny<FilterDefinition<ProjectPeople>>(), It.IsAny<UpdateDefinition<ProjectPeople>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            var result = await CreateRepository().UpdateProjectPeopleOwnerShipAsync(["pp-1"], true);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateProjectOwnerShipAsync_ReassignsTheTenantCreator()
        {
            _tenantsCollection.Setup(c => c.UpdateManyAsync(
                    It.IsAny<FilterDefinition<Tenant>>(), It.IsAny<UpdateDefinition<Tenant>>(),
                    It.IsAny<UpdateOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            var result = await CreateRepository().UpdateProjectOwnerShipAsync(["DTENANT-1"], "user-2");

            result.Should().BeTrue();
        }

        #endregion
    }
}
