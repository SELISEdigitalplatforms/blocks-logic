using Blocks.Genesis;
using DomainService.ManagedService;
using DomainService.ManagedService.Validator;
using DomainService.People;
using DomainService.Projects;
using FluentAssertions;
using FluentValidation.TestHelper;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class UpdateProjectRequestValidatorTests : IDisposable
    {
        private readonly Mock<IProjectRepository> _repository = new();

        public UpdateProjectRequestValidatorTests()
        {
            TestBlocksContext.Set();
            _repository.Setup(r => r.GetByTenantIdAsync(It.IsAny<string>())).ReturnsAsync(CurrentProject("https://old.test"));
            _repository.Setup(r => r.GetByDomainAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private static Tenant CurrentProject(string domain) => new()
        {
            TenantId = "tenant-123",
            DbConnectionString = "mongodb://localhost",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "pw", IssueDate = DateTime.UtcNow },
            Applications = [new Applications { Domain = domain }]
        };

        private UpdateProjectRequestValidator CreateValidator() => new(_repository.Object);

        [Fact]
        public async Task ChangedDomain_ThatIsUnique_Passes()
        {
            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest { ApplicationDomain = "https://new.test" });

            result.ShouldNotHaveValidationErrorFor(x => x.ApplicationDomain);
        }

        [Fact]
        public async Task ChangedDomain_ThatIsNotAUrl_Fails()
        {
            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest { ApplicationDomain = "not a url" });

            result.ShouldHaveValidationErrorFor(x => x.ApplicationDomain)
                .WithErrorMessage("ApplicationDomain is not in a valid format.");
        }

        [Fact]
        public async Task ChangedDomain_AlreadyTakenByAnotherProject_Fails()
        {
            _repository.Setup(r => r.GetByDomainAsync("https://new.test")).ReturnsAsync(CurrentProject("https://new.test"));

            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest { ApplicationDomain = "https://new.test" });

            result.ShouldHaveValidationErrorFor(x => x.ApplicationDomain)
                .WithErrorMessage("ApplicationDomain must be unique");
        }

        [Fact]
        public async Task UnchangedDomain_SkipsTheDomainRules()
        {
            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest { ApplicationDomain = "https://old.test" });

            result.ShouldNotHaveValidationErrorFor(x => x.ApplicationDomain);
            _repository.Verify(r => r.GetByDomainAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task MissingProject_TreatsDomainAsChanged()
        {
            _repository.Setup(r => r.GetByTenantIdAsync(It.IsAny<string>())).ReturnsAsync((Tenant)null!);

            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest { ApplicationDomain = "https://new.test" });

            result.ShouldNotHaveValidationErrorFor(x => x.ApplicationDomain);
            _repository.Verify(r => r.GetByDomainAsync("https://new.test"), Times.Once);
        }

        [Fact]
        public async Task InvalidCustomDomain_Fails()
        {
            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest
            {
                ApplicationDomain = "https://old.test",
                CustomDomain = "not a url"
            });

            result.ShouldHaveValidationErrorFor(x => x.CustomDomain)
                .WithErrorMessage("CustomDomain is not in a valid format.");
        }

        [Fact]
        public async Task ValidCustomDomain_Passes()
        {
            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest
            {
                ApplicationDomain = "https://old.test",
                CustomDomain = "https://custom.test"
            });

            result.ShouldNotHaveValidationErrorFor(x => x.CustomDomain);
        }

        [Fact]
        public async Task BlankCustomDomain_SkipsTheCustomDomainRule()
        {
            var result = await CreateValidator().TestValidateAsync(new UpdateProjectRequest
            {
                ApplicationDomain = "https://old.test",
                CustomDomain = "  "
            });

            result.ShouldNotHaveValidationErrorFor(x => x.CustomDomain);
        }
    }

    public class UpdateAuthConfigRequestValidatorTests
    {
        private readonly UpdateAuthConfigRequestValidator _validator = new();

        private static UpdateAuthConfigRequest Valid() => new()
        {
            ProjectId = "TENANT-1",
            RefreshTokenValidForNumberMinutes = 60,
            GetNumberOfWrongAttemptsToLockTheAccount = 5,
            AccountLockDurationInMinutes = 15,
            AllowedGrantTypes = ["password"]
        };

        [Fact]
        public async Task ValidRequest_Passes()
        {
            var result = await _validator.TestValidateAsync(Valid());

            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task NonPositiveRefreshTokenLifetime_Fails(int minutes)
        {
            var request = Valid();
            request.RefreshTokenValidForNumberMinutes = minutes;

            var result = await _validator.TestValidateAsync(request);

            result.ShouldHaveValidationErrorFor(x => x.RefreshTokenValidForNumberMinutes)
                .WithErrorMessage("Value must be greater than zero.");
        }

        [Fact]
        public async Task NonPositiveWrongAttemptLimit_Fails()
        {
            var request = Valid();
            request.GetNumberOfWrongAttemptsToLockTheAccount = 0;

            var result = await _validator.TestValidateAsync(request);

            result.ShouldHaveValidationErrorFor(x => x.GetNumberOfWrongAttemptsToLockTheAccount);
        }

        [Fact]
        public async Task NonPositiveLockDuration_Fails()
        {
            var request = Valid();
            request.AccountLockDurationInMinutes = 0;

            var result = await _validator.TestValidateAsync(request);

            result.ShouldHaveValidationErrorFor(x => x.AccountLockDurationInMinutes);
        }

        [Fact]
        public async Task EmptyProjectId_Fails()
        {
            var request = Valid();
            request.ProjectId = string.Empty;

            var result = await _validator.TestValidateAsync(request);

            result.ShouldHaveValidationErrorFor(x => x.ProjectId)
                .WithErrorMessage("ProjectId is required.");
        }

        [Fact]
        public async Task NoGrantTypes_Fails()
        {
            var request = Valid();
            request.AllowedGrantTypes = [];

            var result = await _validator.TestValidateAsync(request);

            result.ShouldHaveValidationErrorFor(x => x.AllowedGrantTypes)
                .WithErrorMessage("Atleast one grantType is required");
        }

        [Fact]
        public async Task NullGrantTypes_Fails()
        {
            var request = Valid();
            request.AllowedGrantTypes = null!;

            var result = await _validator.TestValidateAsync(request);

            result.ShouldHaveValidationErrorFor(x => x.AllowedGrantTypes);
        }
    }

    public class TransferOwnershipRequestValidatorTests
    {
        private readonly Mock<IUserRepository> _userRepository = new();

        public TransferOwnershipRequestValidatorTests()
        {
            _userRepository.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { ItemId = "user-2", Email = "new@example.com" });
        }

        private TransferOwnershipRequestValidator CreateValidator() => new(_userRepository.Object);

        [Fact]
        public async Task ValidRequest_Passes()
        {
            var result = await CreateValidator().TestValidateAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = "new@example.com"
            });

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyTenantGroupId_Fails()
        {
            var result = await CreateValidator().TestValidateAsync(new TransferOwnershipRequest
            {
                TenantGroupId = string.Empty,
                TransferToUserEmail = "new@example.com"
            });

            result.ShouldHaveValidationErrorFor(x => x.TenantGroupId)
                .WithErrorMessage("TenantGroupId is required.");
        }

        [Fact]
        public async Task EmptyEmail_Fails()
        {
            var result = await CreateValidator().TestValidateAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = string.Empty
            });

            result.ShouldHaveValidationErrorFor(x => x.TransferToUserEmail)
                .WithErrorMessage("TransferToUserEmail is required.");
        }

        [Fact]
        public async Task MalformedEmail_Fails()
        {
            var result = await CreateValidator().TestValidateAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = "not-an-email"
            });

            result.ShouldHaveValidationErrorFor(x => x.TransferToUserEmail)
                .WithErrorMessage("TransferToUserEmail must be a valid email address.");
        }

        [Fact]
        public async Task UnknownUser_Fails()
        {
            _userRepository.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User)null!);

            var result = await CreateValidator().TestValidateAsync(new TransferOwnershipRequest
            {
                TenantGroupId = "group-1",
                TransferToUserEmail = "ghost@example.com"
            });

            result.ShouldHaveValidationErrorFor(x => x.TransferToUserEmail)
                .WithErrorMessage("Must be an existing user");
        }
    }

    public class RegisterServiceRequestValidatorTests
    {
        private readonly RegisterServiceRequestValidator _validator = new();

        [Theory]
        [InlineData("backend")]
        [InlineData("frontend")]
        [InlineData("BACKEND")]
        public async Task AllowedServiceType_Passes(string serviceType)
        {
            var result = await _validator.TestValidateAsync(new RegisterServiceRequest
            {
                ServiceName = "logs-collector",
                ServiceType = serviceType
            });

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyServiceName_Fails()
        {
            var result = await _validator.TestValidateAsync(new RegisterServiceRequest
            {
                ServiceName = string.Empty,
                ServiceType = "backend"
            });

            result.ShouldHaveValidationErrorFor(x => x.ServiceName)
                .WithErrorMessage("ServiceName is required.");
        }

        [Fact]
        public async Task ServiceNameOverOneHundredCharacters_Fails()
        {
            var result = await _validator.TestValidateAsync(new RegisterServiceRequest
            {
                ServiceName = new string('a', 101),
                ServiceType = "backend"
            });

            result.ShouldHaveValidationErrorFor(x => x.ServiceName)
                .WithErrorMessage("ServiceName cannot exceed 100 characters.");
        }

        [Fact]
        public async Task EmptyServiceType_Fails()
        {
            var result = await _validator.TestValidateAsync(new RegisterServiceRequest
            {
                ServiceName = "logs-collector",
                ServiceType = string.Empty
            });

            result.ShouldHaveValidationErrorFor(x => x.ServiceType)
                .WithErrorMessage("ServiceType is required.");
        }

        [Fact]
        public async Task UnknownServiceType_Fails()
        {
            var result = await _validator.TestValidateAsync(new RegisterServiceRequest
            {
                ServiceName = "logs-collector",
                ServiceType = "sidecar"
            });

            result.ShouldHaveValidationErrorFor(x => x.ServiceType);
        }
    }
}
