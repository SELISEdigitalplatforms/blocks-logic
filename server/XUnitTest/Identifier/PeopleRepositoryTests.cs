using Blocks.Genesis;
using DomainService.Entities;
using DomainService.People;
using DomainService.Projects;
using FluentAssertions;
using Identifier.DomainService.Shared.Entities.Iam.DomainService.Entities;
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

        public PeopleRepositoryTests()
        {
            TestBlocksContext.Set();
            _dbContextProvider.Setup(p => p.GetCollection<ProjectPeople>(PeopleCollectionName)).Returns(_peoples.Object);
            _dbContextProvider.Setup(p => p.GetCollection<User>(UserCollectionName)).Returns(_users.Object);
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
    }
}
