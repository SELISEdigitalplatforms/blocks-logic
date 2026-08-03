using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.People;
using DomainService.Projects;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class PeopleServiceTests : IDisposable
    {
        private readonly Mock<ILogger<PeopleService>> _logger = new();
        private readonly Mock<IPeopleRepository> _peopleRepository = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IUserManagementMutationService> _iamDriverService = new();
        private readonly Mock<IValidator<SignupRequest>> _signupValidator = new();
        private readonly Mock<IValidator<TransferOwnershipRequest>> _transferValidator = new();
        private readonly Mock<IProjectRepository> _projectRepository = new();
        private readonly Mock<ITenants> _tenants = new();

        public PeopleServiceTests()
        {
            TestBlocksContext.Set();
            _configuration.Setup(c => c["BlocksAppHost"]).Returns("https://app.test");
            _signupValidator.Setup(v => v.ValidateAsync(It.IsAny<SignupRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _transferValidator.Setup(v => v.ValidateAsync(It.IsAny<TransferOwnershipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        public void Dispose() => TestBlocksContext.Clear();

        private PeopleService CreateService() => new(
            _logger.Object,
            _peopleRepository.Object,
            _messageClient.Object,
            _configuration.Object,
            _cacheClient.Object,
            _iamDriverService.Object,
            _signupValidator.Object,
            _transferValidator.Object,
            _projectRepository.Object,
            _tenants.Object);

        private static Tenant Project(string tenantId = "TENANT-1", string name = "Demo") => new()
        {
            ItemId = "project-item-1",
            TenantId = tenantId,
            Name = name,
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "pw", IssueDate = DateTime.UtcNow },
            Applications = [new Applications { Domain = "https://demo.test", CookieDomain = "demo.test" }]
        };

        private static User Person(string itemId = "user-1", string email = "invitee@example.com") => new()
        {
            ItemId = itemId,
            Email = email,
            FirstName = "Ada",
            LastName = "Lovelace"
        };

        #region Constructor

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new PeopleService(null!, _peopleRepository.Object, _messageClient.Object,
                _configuration.Object, _cacheClient.Object, _iamDriverService.Object, _signupValidator.Object,
                _transferValidator.Object, _projectRepository.Object, _tenants.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_NullPeopleRepository_Throws()
        {
            Action act = () => new PeopleService(_logger.Object, null!, _messageClient.Object,
                _configuration.Object, _cacheClient.Object, _iamDriverService.Object, _signupValidator.Object,
                _transferValidator.Object, _projectRepository.Object, _tenants.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_NullMessageClient_Throws()
        {
            Action act = () => new PeopleService(_logger.Object, _peopleRepository.Object, null!,
                _configuration.Object, _cacheClient.Object, _iamDriverService.Object, _signupValidator.Object,
                _transferValidator.Object, _projectRepository.Object, _tenants.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_NullConfiguration_Throws()
        {
            Action act = () => new PeopleService(_logger.Object, _peopleRepository.Object, _messageClient.Object,
                null!, _cacheClient.Object, _iamDriverService.Object, _signupValidator.Object,
                _transferValidator.Object, _projectRepository.Object, _tenants.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_NullCacheClient_Throws()
        {
            Action act = () => new PeopleService(_logger.Object, _peopleRepository.Object, _messageClient.Object,
                _configuration.Object, null!, _iamDriverService.Object, _signupValidator.Object,
                _transferValidator.Object, _projectRepository.Object, _tenants.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_NullIamDriverService_Throws()
        {
            Action act = () => new PeopleService(_logger.Object, _peopleRepository.Object, _messageClient.Object,
                _configuration.Object, _cacheClient.Object, null!, _signupValidator.Object,
                _transferValidator.Object, _projectRepository.Object, _tenants.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_NullSignupValidator_Throws()
        {
            Action act = () => new PeopleService(_logger.Object, _peopleRepository.Object, _messageClient.Object,
                _configuration.Object, _cacheClient.Object, _iamDriverService.Object, null!,
                _transferValidator.Object, _projectRepository.Object, _tenants.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region GetPeoplesAsync

        [Fact]
        public async Task GetPeoplesAsync_NullRequest_ReturnsEmptyGroupIdError()
        {
            var result = await CreateService().GetPeoplesAsync(null!);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("empty_group_id");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetPeoplesAsync_BlankGroupId_ReturnsEmptyGroupIdError(string groupId)
        {
            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = groupId });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("empty_group_id");
        }

        [Fact]
        public async Task GetPeoplesAsync_NoSharedProjects_ReturnsNoProjectsError()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync(new List<global::DomainService.Entities.Project>());

            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("no_projects");
            _peopleRepository.Verify(r => r.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>()), Times.Never);
        }

        [Fact]
        public async Task GetPeoplesAsync_NullSharedProjects_ReturnsNoProjectsError()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync((List<global::DomainService.Entities.Project>)null!);

            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            result.Errors.Should().ContainKey("no_projects");
        }

        [Fact]
        public async Task GetPeoplesAsync_GroupsPeopleByDetails()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync([new global::DomainService.Entities.Project { TenantId = "TENANT-1" }]);

            var details = new PeopleDetails { UserId = "user-1", Email = "invitee@example.com" };
            _peopleRepository.Setup(r => r.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>()))
                .ReturnsAsync((new List<GetProjectPeople>
                {
                    new() { ItemId = "pp-1", TenantId = "TENANT-1", Enviroment = "dev", peopleDetails = details, IsCreator = true },
                    new() { ItemId = "pp-2", TenantId = "TENANT-2", Enviroment = "stg", peopleDetails = details, IsInvitationSent = true }
                }, 2, 1, true));

            var result = await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            result.IsSuccess.Should().BeTrue();
            result.IsOwner.Should().BeTrue();
            result.TotalCount.Should().Be(2);
            result.PeoplesTotalCount.Should().Be(1);
            result.Peoples.Should().HaveCount(1);
            result.Peoples[0].SharedEnviroments.Should().HaveCount(2);
            result.Peoples[0].SharedEnviroments.Select(e => e.Enviroment).Should().BeEquivalentTo(["dev", "stg"]);
        }

        [Fact]
        public async Task GetPeoplesAsync_RepositoryThrows_Rethrows()
        {
            _projectRepository.Setup(r => r.GetProjectPeoplesAsync("group-1"))
                .ReturnsAsync([new global::DomainService.Entities.Project { TenantId = "TENANT-1" }]);
            _peopleRepository.Setup(r => r.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().GetPeoplesAsync(new GetPeoplesRequest { ProjectGroupId = "group-1" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region InvitePeoplesAsync

        [Fact]
        public async Task InvitePeoplesAsync_NullRequest_ReturnsInvalidGroupId()
        {
            var result = await CreateService().InvitePeoplesAsync(null!);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_group_id");
        }

        [Fact]
        public async Task InvitePeoplesAsync_NoTenantsForGroup_ReturnsInvalidGroupId()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(new List<string>());

            var result = await CreateService().InvitePeoplesAsync(new InviteRequest { GroupId = "group-1" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_group_id");
        }

        [Fact]
        public async Task InvitePeoplesAsync_NotOwner_ReturnsOwnProjectError()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(false);

            var result = await CreateService().InvitePeoplesAsync(new InviteRequest { GroupId = "group-1" });

            result.Errors.Should().ContainKey("own_project");
        }

        [Fact]
        public async Task InvitePeoplesAsync_SkipsBlankAndSelfEmails()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>>
                {
                    { "  ", ["TENANT-1"] },
                    { "testuser", ["TENANT-1"] }
                }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()), Times.Never);
        }

        [Fact]
        public async Task InvitePeoplesAsync_NoValidProjectKeys_SkipsEmail()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>> { { "invitee@example.com", ["OTHER-TENANT"] } }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()), Times.Never);
        }

        [Fact]
        public async Task InvitePeoplesAsync_NullProjectKeys_SkipsEmail()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>> { { "invitee@example.com", null! } }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()), Times.Never);
        }

        [Fact]
        public async Task InvitePeoplesAsync_UnknownUser_SendsUserCreationEvent()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<User>());

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>> { { "invitee@example.com", ["TENANT-1"] } }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _messageClient.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<CreateUserByEmailEvent_Identifier>>(c => c.Payload.Email == "invitee@example.com")),
                Times.Once);
            _peopleRepository.Verify(r => r.InsertPeoplesAsync(It.IsAny<List<ProjectPeople>>()), Times.Never);
        }

        [Fact]
        public async Task InvitePeoplesAsync_ExistingUserFirstInvitation_InsertsAndSendsInvitationMail()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-1", It.IsAny<List<string>>()))
                .ReturnsAsync(new List<ProjectPeople>());
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1")).ReturnsAsync(Project());

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>> { { "invitee@example.com", ["TENANT-1"] } }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.InsertPeoplesAsync(It.Is<List<ProjectPeople>>(
                p => p.Count == 1 && p[0].TenantId == "TENANT-1" && p[0].IsInvitationSent && !p[0].IsInvitationConfirmed)), Times.Once);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()), Times.Once);
            _cacheClient.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 3600), Times.Once);
        }

        [Fact]
        public async Task InvitePeoplesAsync_ExistingUserWithPriorAccess_MarksNewProjectsConfirmed()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1", "TENANT-2"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-1", It.IsAny<List<string>>()))
                .ReturnsAsync([new ProjectPeople { ItemId = "pp-existing", TenantId = "TENANT-1", UserId = "user-1" }]);

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>> { { "invitee@example.com", ["TENANT-1", "TENANT-2"] } }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.InsertPeoplesAsync(It.Is<List<ProjectPeople>>(
                p => p.Count == 1 && p[0].TenantId == "TENANT-2" && p[0].IsInvitationConfirmed)), Times.Once);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()), Times.Never);
        }

        [Fact]
        public async Task InvitePeoplesAsync_UserAlreadyHasAllProjects_InsertsNothing()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-1", It.IsAny<List<string>>()))
                .ReturnsAsync([new ProjectPeople { ItemId = "pp-existing", TenantId = "TENANT-1", UserId = "user-1" }]);

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>> { { "invitee@example.com", ["TENANT-1"] } }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.InsertPeoplesAsync(It.IsAny<List<ProjectPeople>>()), Times.Never);
        }

        [Fact]
        public async Task InvitePeoplesAsync_FirstInvitationButProjectMissing_SkipsMail()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-1", It.IsAny<List<string>>()))
                .ReturnsAsync((List<ProjectPeople>)null!);
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1")).ReturnsAsync((Tenant)null!);

            var request = new InviteRequest
            {
                GroupId = "group-1",
                Invitations = new Dictionary<string, List<string>> { { "invitee@example.com", ["TENANT-1"] } }
            };

            var result = await CreateService().InvitePeoplesAsync(request);

            result.IsSuccess.Should().BeTrue();
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()), Times.Never);
        }

        [Fact]
        public async Task InvitePeoplesAsync_RepositoryThrows_Rethrows()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1"))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().InvitePeoplesAsync(new InviteRequest { GroupId = "group-1" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region ProcessUserCreateAndInvitation / ProcessInvitation / SendInvitationEmail

        [Fact]
        public async Task ProcessUserCreateAndInvitation_EmptyEmail_ReturnsFalse()
        {
            var result = await CreateService().ProcessUserCreateAndInvitation("   ", "TENANT-1");

            result.Should().BeFalse();
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateUserByEmailEvent_Identifier>>()), Times.Never);
        }

        [Fact]
        public async Task ProcessUserCreateAndInvitation_MessageClientThrows_Rethrows()
        {
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateUserByEmailEvent_Identifier>>()))
                .ThrowsAsync(new InvalidOperationException("bus down"));

            var act = async () => await CreateService().ProcessUserCreateAndInvitation("a@b.com", "TENANT-1");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task ProcessInvitation_NullUser_ReturnsFalse()
        {
            var result = await CreateService().ProcessInvitation(null!, ["pp-1"], Project(), string.Empty);

            result.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessInvitation_NullProject_ReturnsFalse()
        {
            var result = await CreateService().ProcessInvitation(Person(), ["pp-1"], null!, string.Empty);

            result.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessInvitation_MailFails_Rethrows()
        {
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()))
                .ThrowsAsync(new InvalidOperationException("mail down"));

            var act = async () => await CreateService().ProcessInvitation(Person(), ["pp-1"], Project(), "key");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task SendInvitationEmail_UsesConfiguredHostAndProjectName()
        {
            global::DomainService.Dtos.SendMail? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()))
                .Callback<ConsumerMessage<global::DomainService.Dtos.SendMail>>(c => captured = c.Payload)
                .Returns(Task.CompletedTask);

            var code = await CreateService().SendInvitationEmail(Person(), Project());

            code.Should().NotBeNullOrWhiteSpace();
            captured.Should().NotBeNull();
            captured!.BodyDataContext["ProjectInvitationLink"].Should().Be($"https://app.test/invitation?code={code}");
            captured.BodyDataContext["DisplayName"].Should().Be("Ada Lovelace");
            captured.BodyDataContext["ProjectName"].Should().Be("Demo");
            captured.To.Should().BeEquivalentTo(["invitee@example.com"]);
        }

        [Fact]
        public async Task SendInvitationEmail_MissingHostAndNames_FallsBackToDefaults()
        {
            _configuration.Setup(c => c["BlocksAppHost"]).Returns((string)null!);

            global::DomainService.Dtos.SendMail? captured = null;
            _messageClient.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()))
                .Callback<ConsumerMessage<global::DomainService.Dtos.SendMail>>(c => captured = c.Payload)
                .Returns(Task.CompletedTask);

            var user = new User { ItemId = "user-1", Email = "Invitee@Example.com" };
            var project = Project();
            project.Name = string.Empty;

            var code = await CreateService().SendInvitationEmail(user, project);

            captured!.BodyDataContext["ProjectInvitationLink"].Should().Be($"https://app.blocks.com/invitation?code={code}");
            captured.BodyDataContext["DisplayName"].Should().Be("Invitee@Example.com");
            captured.BodyDataContext["ProjectName"].Should().Be("https://demo.test");
            captured.To.Should().BeEquivalentTo(["invitee@example.com"]);
        }

        #endregion

        #region RemoveAccessFromProjectAsync

        [Fact]
        public async Task RemoveAccessFromProjectAsync_NullRequest_ReturnsInvalidRequest()
        {
            var result = await CreateService().RemoveAccessFromProjectAsync(null!);

            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_NoProjectKeys_ReturnsInvalidRequest()
        {
            var result = await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "a@b.com" });

            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_EmptyEmail_ReturnsInvalidRequest()
        {
            var result = await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "", ProjectKeys = ["TENANT-1"] });

            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_NoTenants_ReturnsInvalidGroupId()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(new List<string>());

            var result = await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "a@b.com", ProjectKeys = ["TENANT-1"] });

            result.Errors.Should().ContainKey("invalid_group_id");
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_NotOwner_ReturnsOwnProjectError()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(false);

            var result = await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "a@b.com", ProjectKeys = ["TENANT-1"] });

            result.Errors.Should().ContainKey("own_project");
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_ProjectKeysOutsideGroup_ReturnsInvalidGroupId()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);

            var result = await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "a@b.com", ProjectKeys = ["OTHER"] });

            result.Errors.Should().ContainKey("invalid_group_id");
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_UserNotFound_ReturnsUserNotFound()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<User>());

            var result = await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "invitee@example.com", ProjectKeys = ["TENANT-1"] });

            result.Errors.Should().ContainKey("user_not_found");
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_ValidRequest_RemovesPeople()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.RemovePeoplesAsync("invitee@example.com", It.IsAny<List<string>>())).ReturnsAsync(true);

            var result = await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "invitee@example.com", ProjectKeys = ["TENANT-1", "OTHER"] });

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.RemovePeoplesAsync("invitee@example.com",
                It.Is<List<string>>(k => k.Count == 1 && k[0] == "TENANT-1")), Times.Once);
        }

        [Fact]
        public async Task RemoveAccessFromProjectAsync_RepositoryThrows_Rethrows()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().RemoveAccessFromProjectAsync(
                new RemoveAccessRequest { GroupId = "group-1", Email = "a@b.com", ProjectKeys = ["TENANT-1"] });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region SendProjectInvitationToNewUser

        [Fact]
        public async Task SendProjectInvitationToNewUser_NullEvent_ReturnsFalse()
        {
            var result = await CreateService().SendProjectInvitationToNewUser(null!);

            result.Should().BeFalse();
        }

        [Theory]
        [InlineData("", "TENANT-1")]
        [InlineData("user-1", "")]
        public async Task SendProjectInvitationToNewUser_MissingFields_ReturnsFalse(string userId, string projectKey)
        {
            var result = await CreateService().SendProjectInvitationToNewUser(
                new CreateUserByEmailPostEvent_Identifier { UserId = userId, ProjectKey = projectKey });

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SendProjectInvitationToNewUser_OnlySeparators_ReturnsFalse()
        {
            var result = await CreateService().SendProjectInvitationToNewUser(
                new CreateUserByEmailPostEvent_Identifier { UserId = "user-1", ProjectKey = ";;;" });

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SendProjectInvitationToNewUser_ProjectNotFound_ReturnsFalse()
        {
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1")).ReturnsAsync((Tenant)null!);

            var result = await CreateService().SendProjectInvitationToNewUser(
                new CreateUserByEmailPostEvent_Identifier { UserId = "user-1", ProjectKey = "TENANT-1" });

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SendProjectInvitationToNewUser_UserNotFound_ReturnsFalse()
        {
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1")).ReturnsAsync(Project());
            _peopleRepository.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync((User)null!);

            var result = await CreateService().SendProjectInvitationToNewUser(
                new CreateUserByEmailPostEvent_Identifier { UserId = "user-1", ProjectKey = "TENANT-1" });

            result.Should().BeFalse();
        }

        [Fact]
        public async Task SendProjectInvitationToNewUser_ValidEvent_InsertsPeopleAndSendsMail()
        {
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1")).ReturnsAsync(Project());
            _peopleRepository.Setup(r => r.GetUserByIdAsync("user-1")).ReturnsAsync(Person());

            var result = await CreateService().SendProjectInvitationToNewUser(
                new CreateUserByEmailPostEvent_Identifier { UserId = "user-1", ProjectKey = "TENANT-1;TENANT-2", Key = "activation" });

            result.Should().BeTrue();
            _peopleRepository.Verify(r => r.InsertPeoplesAsync(It.Is<List<ProjectPeople>>(p => p.Count == 2)), Times.Once);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()), Times.Once);
        }

        [Fact]
        public async Task SendProjectInvitationToNewUser_RepositoryThrows_Rethrows()
        {
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1"))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().SendProjectInvitationToNewUser(
                new CreateUserByEmailPostEvent_Identifier { UserId = "user-1", ProjectKey = "TENANT-1" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region ConfirmInvitationAsync

        [Fact]
        public async Task ConfirmInvitationAsync_NullRequest_ReturnsInvalidCode()
        {
            var result = await CreateService().ConfirmInvitationAsync(null!);

            result.Errors.Should().ContainKey("invalid_code");
        }

        [Fact]
        public async Task ConfirmInvitationAsync_UnknownCode_ReturnsCodeExpired()
        {
            _cacheClient.Setup(c => c.GetStringValueAsync("code-1")).ReturnsAsync(string.Empty);

            var result = await CreateService().ConfirmInvitationAsync(new ConfirmInvitationRequest { Code = "code-1" });

            result.Errors.Should().ContainKey("code_expire");
        }

        [Fact]
        public async Task ConfirmInvitationAsync_CachedDataWithoutIds_ReturnsInvalidData()
        {
            _cacheClient.Setup(c => c.GetStringValueAsync("code-1"))
                .ReturnsAsync(JsonSerializer.Serialize(new CacheProjectPeopleInvitation { ProjectPeopleIds = "" }));

            var result = await CreateService().ConfirmInvitationAsync(new ConfirmInvitationRequest { Code = "code-1" });

            result.Errors.Should().ContainKey("invalid_data");
        }

        [Fact]
        public async Task ConfirmInvitationAsync_ValidCode_ConfirmsAndClearsCache()
        {
            _cacheClient.Setup(c => c.GetStringValueAsync("code-1"))
                .ReturnsAsync(JsonSerializer.Serialize(new CacheProjectPeopleInvitation
                {
                    ProjectPeopleIds = "pp-1;pp-2",
                    UserActivationKey = "activation-key"
                }));

            var result = await CreateService().ConfirmInvitationAsync(new ConfirmInvitationRequest { Code = "code-1" });

            result.IsSuccess.Should().BeTrue();
            result.ActivationKey.Should().Be("activation-key");
            _peopleRepository.Verify(r => r.UpdateProjectPeoples(It.Is<List<string>>(i => i.Count == 2)), Times.Once);
            _cacheClient.Verify(c => c.RemoveKeyAsync("code-1"), Times.Once);
        }

        [Fact]
        public async Task ConfirmInvitationAsync_CacheThrows_Rethrows()
        {
            _cacheClient.Setup(c => c.GetStringValueAsync("code-1"))
                .ThrowsAsync(new InvalidOperationException("cache down"));

            var act = async () => await CreateService().ConfirmInvitationAsync(new ConfirmInvitationRequest { Code = "code-1" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region ResendInvitationAsync

        [Fact]
        public async Task ResendInvitationAsync_NullRequest_ReturnsInvalidRequest()
        {
            var result = await CreateService().ResendInvitationAsync(null!);

            result.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task ResendInvitationAsync_NoTenants_ReturnsInvalidGroupId()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(new List<string>());

            var result = await CreateService().ResendInvitationAsync(
                new ResendInvitationRequest { GroupId = "group-1", Email = "a@b.com" });

            result.Errors.Should().ContainKey("invalid_group_id");
        }

        [Fact]
        public async Task ResendInvitationAsync_NotOwner_ReturnsOwnProjectError()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(false);

            var result = await CreateService().ResendInvitationAsync(
                new ResendInvitationRequest { GroupId = "group-1", Email = "a@b.com" });

            result.Errors.Should().ContainKey("own_project");
        }

        [Fact]
        public async Task ResendInvitationAsync_UserNotFound_ReturnsUserNotFound()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<User>());

            var result = await CreateService().ResendInvitationAsync(
                new ResendInvitationRequest { GroupId = "group-1", Email = "invitee@example.com" });

            result.Errors.Should().ContainKey("user_not_found");
        }

        [Fact]
        public async Task ResendInvitationAsync_NoExistingInvitation_ReturnsInvitationNotFound()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-1", It.IsAny<List<string>>()))
                .ReturnsAsync(new List<ProjectPeople>());

            var result = await CreateService().ResendInvitationAsync(
                new ResendInvitationRequest { GroupId = "group-1", Email = "invitee@example.com" });

            result.Errors.Should().ContainKey("invitation_not_found");
        }

        [Fact]
        public async Task ResendInvitationAsync_ProjectNotFound_ReturnsProjectNotFound()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-1", It.IsAny<List<string>>()))
                .ReturnsAsync([new ProjectPeople { ItemId = "pp-1", TenantId = "TENANT-1" }]);
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1")).ReturnsAsync((Tenant)null!);

            var result = await CreateService().ResendInvitationAsync(
                new ResendInvitationRequest { GroupId = "group-1", Email = "invitee@example.com" });

            result.Errors.Should().ContainKey("project_not_found");
        }

        [Fact]
        public async Task ResendInvitationAsync_ValidRequest_SendsInvitationAgain()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync([Person()]);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-1", It.IsAny<List<string>>()))
                .ReturnsAsync([new ProjectPeople { ItemId = "pp-1", TenantId = "TENANT-1" }]);
            _peopleRepository.Setup(r => r.GetProjectByIdAsync("TENANT-1")).ReturnsAsync(Project());

            var result = await CreateService().ResendInvitationAsync(
                new ResendInvitationRequest { GroupId = "group-1", Email = "invitee@example.com" });

            result.IsSuccess.Should().BeTrue();
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::DomainService.Dtos.SendMail>>()), Times.Once);
        }

        [Fact]
        public async Task ResendInvitationAsync_RepositoryThrows_Rethrows()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1"))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().ResendInvitationAsync(
                new ResendInvitationRequest { GroupId = "group-1", Email = "a@b.com" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region SignupAsync

        [Fact]
        public async Task SignupAsync_InvalidRequest_ReturnsValidationErrors()
        {
            _signupValidator.Setup(v => v.ValidateAsync(It.IsAny<SignupRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult([new ValidationFailure("Email", "Email is required")]));

            var result = await CreateService().SignupAsync(new SignupRequest { Email = "" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Email");
        }

        [Fact]
        public async Task SignupAsync_ActiveVerifiedUserExists_ReturnsAlreadySignedUp()
        {
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()))
                .ReturnsAsync([new User { ItemId = "user-1", Email = "a@b.com", Active = true, IsVarified = true }]);

            var result = await CreateService().SignupAsync(new SignupRequest { Email = "a@b.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("already_signup");
            _iamDriverService.Verify(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>()), Times.Never);
        }

        [Fact]
        public async Task SignupAsync_UnverifiedUserExists_ContinuesToCreateUser()
        {
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()))
                .ReturnsAsync([new User { ItemId = "user-1", Email = "a@b.com", Active = true, IsVarified = false }]);
            _iamDriverService.Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateService().SignupAsync(new SignupRequest { Email = "a@b.com" });

            result.IsSuccess.Should().BeTrue();
            _iamDriverService.Verify(s => s.CreateUserAsync(It.Is<CreateUserRequest>(c => c.Email == "a@b.com")), Times.Once);
        }

        [Fact]
        public async Task SignupAsync_CreateUserReturnsNull_ReturnsCreationFailed()
        {
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<User>());
            _iamDriverService.Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync((BaseMutationResponse)null!);

            var result = await CreateService().SignupAsync(new SignupRequest { Email = "a@b.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("creation_failed");
        }

        [Fact]
        public async Task SignupAsync_NewUser_ReturnsIamResult()
        {
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<User>());
            _iamDriverService.Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "iam", "nope" } }
                });

            var result = await CreateService().SignupAsync(new SignupRequest { Email = "a@b.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("iam");
        }

        [Fact]
        public async Task SignupAsync_ValidatorThrows_Rethrows()
        {
            _signupValidator.Setup(v => v.ValidateAsync(It.IsAny<SignupRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().SignupAsync(new SignupRequest { Email = "a@b.com" });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        #endregion

        #region TransferOwnershipAsync

        [Fact]
        public async Task TransferOwnershipAsync_InvalidRequest_ReturnsValidationErrors()
        {
            _transferValidator.Setup(v => v.ValidateAsync(It.IsAny<TransferOwnershipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult([new ValidationFailure("TenantGroupId", "required")]));

            var result = await CreateService().TransferOwnershipAsync(new TransferOwnershipRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("TenantGroupId");
        }

        [Fact]
        public async Task TransferOwnershipAsync_NotOwner_ReturnsOwnProjectError()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(false);

            var result = await CreateService().TransferOwnershipAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = "new@example.com"
            });

            result.Errors.Should().ContainKey("own_project");
        }

        [Fact]
        public async Task TransferOwnershipAsync_TransferToSelf_ReturnsOwnProjectError()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);

            var result = await CreateService().TransferOwnershipAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = "testuser"
            });

            result.Errors.Should().ContainKey("own_project");
        }

        [Fact]
        public async Task TransferOwnershipAsync_NewOwnerHasNoRecord_InsertsCreatorRecord()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-123", It.IsAny<List<string>>()))
                .ReturnsAsync([new ProjectPeople { ItemId = "pp-owner", TenantId = "TENANT-1", UserId = "user-123" }]);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()))
                .ReturnsAsync([new User { ItemId = "user-2", Email = "new@example.com" }]);
            _peopleRepository.Setup(r => r.GetProjectPeopleByTenantIdAndUserIdAsync("TENANT-1", "user-2"))
                .ReturnsAsync((ProjectPeople)null!);

            var result = await CreateService().TransferOwnershipAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = "new@example.com"
            });

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.UpdateProjectPeopleOwnerShipAsync(
                It.Is<List<string>>(i => i.Contains("pp-owner")), false), Times.Once);
            _peopleRepository.Verify(r => r.UpdateProjectOwnerShipAsync(It.IsAny<List<string>>(), "user-2"), Times.Once);
            _peopleRepository.Verify(r => r.InsertPeoplesAsync(
                It.Is<List<ProjectPeople>>(p => p.Count == 1 && p[0].IsCreator && p[0].UserId == "user-2")), Times.Once);
        }

        [Fact]
        public async Task TransferOwnershipAsync_NewOwnerHasRecord_PromotesExistingRecord()
        {
            _projectRepository.Setup(r => r.GetProjectIdsByGroupId("group-1")).ReturnsAsync(["TENANT-1"]);
            _peopleRepository.Setup(r => r.IsOwner(It.IsAny<string>(), It.IsAny<List<string>>())).ReturnsAsync(true);
            _peopleRepository.Setup(r => r.GetProjectPeoplesAsync("user-123", It.IsAny<List<string>>()))
                .ReturnsAsync([new ProjectPeople { ItemId = "pp-owner", TenantId = "TENANT-1", UserId = "user-123" }]);
            _peopleRepository.Setup(r => r.GetUsersByEmailAsync(It.IsAny<List<string>>()))
                .ReturnsAsync([new User { ItemId = "user-2", Email = "new@example.com" }]);
            _peopleRepository.Setup(r => r.GetProjectPeopleByTenantIdAndUserIdAsync("TENANT-1", "user-2"))
                .ReturnsAsync(new ProjectPeople { ItemId = "pp-new", TenantId = "TENANT-1", UserId = "user-2" });

            var result = await CreateService().TransferOwnershipAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = "new@example.com"
            });

            result.IsSuccess.Should().BeTrue();
            _peopleRepository.Verify(r => r.InsertPeoplesAsync(It.IsAny<List<ProjectPeople>>()), Times.Never);
            _peopleRepository.Verify(r => r.UpdateProjectPeopleOwnerShipAsync(
                It.Is<List<string>>(i => i.Count == 1 && i[0] == "pp-new"), true), Times.Once);
            _tenants.Verify(t => t.UpdateTenantVersionAsync(It.IsAny<TenantCacheUpdateMessage>()), Times.Once);
        }

        #endregion
    }
}
