using DomainService.Projects;
using FluentValidation.TestHelper;
using Moq;

namespace XUnitTest.Identifier
{
    public class CreateProjectRequestValidatorTests
    {
        private readonly Mock<IProjectRepository> _repo = new();
        private readonly CreateProjectRequestValidator _validator;

        public CreateProjectRequestValidatorTests()
        {
            _repo.Setup(r => r.IsExistingEnviroment(It.IsAny<List<string>>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            _validator = new CreateProjectRequestValidator(_repo.Object);
        }

        private static CreateProjectRequest Valid() => new()
        {
            Name = "My Project",
            IsAcceptBlocksTerms = true,
            IsUseBlocksExclusively = true,
            applicationContexts = new List<ApplicationContext>
            {
                new() { Environment = "dev", Domain = "https://dev.example.com", CookieDomain = "example.com" }
            }
        };

        [Fact]
        public async Task Valid_Passes()
        {
            var result = await _validator.TestValidateAsync(Valid());
            result.ShouldNotHaveValidationErrorFor(x => x.Name);
            result.ShouldNotHaveValidationErrorFor(x => x.applicationContexts);
        }

        [Theory]
        [InlineData("")]
        [InlineData("ab")]
        public async Task ShortOrEmptyName_Fails(string name)
        {
            var req = Valid();
            req.Name = name;
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public async Task NotAcceptingTerms_Fails()
        {
            var req = Valid();
            req.IsAcceptBlocksTerms = false;
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.IsAcceptBlocksTerms);
        }

        [Fact]
        public async Task NotUsingBlocksExclusively_Fails()
        {
            var req = Valid();
            req.IsUseBlocksExclusively = false;
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.IsUseBlocksExclusively);
        }

        [Fact]
        public async Task EmptyApplicationContexts_Fails()
        {
            var req = Valid();
            req.applicationContexts = new List<ApplicationContext>();
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.applicationContexts);
        }

        [Fact]
        public async Task UnsupportedEnvironment_Fails()
        {
            var req = Valid();
            req.applicationContexts[0].Environment = "banana";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.applicationContexts);
        }

        [Fact]
        public async Task InvalidDomain_Fails()
        {
            var req = Valid();
            req.applicationContexts[0].Domain = "not a url";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.applicationContexts);
        }

        [Fact]
        public async Task DuplicateEnvironments_Fails()
        {
            var req = Valid();
            req.applicationContexts = new List<ApplicationContext>
            {
                new() { Environment = "dev", Domain = "https://a.example.com", CookieDomain = "example.com" },
                new() { Environment = "dev", Domain = "https://b.example.com", CookieDomain = "example.com" }
            };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.applicationContexts);
        }

        [Fact]
        public async Task WithTenantGroupId_ExistingEnvironment_Fails()
        {
            _repo.Setup(r => r.IsExistingEnviroment(It.IsAny<List<string>>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            var req = Valid();
            req.TenantGroupId = "group-1";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x);
        }

        [Fact]
        public async Task WithTenantGroupId_NoExistingEnvironment_Passes()
        {
            var req = Valid();
            req.TenantGroupId = "group-1";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.applicationContexts);
        }
    }
}
