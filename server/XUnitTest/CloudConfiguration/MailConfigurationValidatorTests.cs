using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Mail.Validators;
using CloudConfiguration.DomainService.Shared.Services;
using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;

namespace XUnitTest.CloudConfiguration
{
    public class MailConfigurationValidatorTests
    {
        private readonly Mock<IConfigurationRepository> _repo = new();
        private readonly MailConfigurationValidator _validator;

        public MailConfigurationValidatorTests()
        {
            _repo.Setup(r => r.GetMailConfigurationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync((MailConfiguration)null!);
            _validator = new MailConfigurationValidator(_repo.Object);
        }

        private static MailConfiguration ValidOutbound() => new()
        {
            ConfigurationName = "primary",
            ConfigurationId = "id1",
            Host = "smtp.example.com",
            Port = 587,
            SenderName = "Sender",
            SenderAddress = "sender@example.com",
            SenderUserName = "user",
            AccountPassword = "secret1",
            IsInbound = false
        };

        [Fact]
        public async Task ValidOutbound_Passes()
        {
            var result = await _validator.TestValidateAsync(ValidOutbound());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyConfigurationName_Fails()
        {
            var req = ValidOutbound();
            req.ConfigurationName = "";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.ConfigurationName);
        }

        [Fact]
        public async Task NonUniqueName_Fails()
        {
            _repo.Setup(r => r.GetMailConfigurationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(new MailConfiguration());
            var result = await _validator.TestValidateAsync(ValidOutbound());
            result.ShouldHaveValidationErrorFor(x => x.ConfigurationName);
        }

        [Fact]
        public async Task EmptyConfigurationId_Fails()
        {
            var req = ValidOutbound();
            req.ConfigurationId = "";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.ConfigurationId);
        }

        [Fact]
        public async Task InvalidHost_Fails()
        {
            var req = ValidOutbound();
            req.Host = "not a host!!";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Host);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(70000)]
        public async Task PortOutOfRange_Fails(int port)
        {
            var req = ValidOutbound();
            req.Port = port;
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Port);
        }

        [Fact]
        public async Task Outbound_MissingSenderName_Fails()
        {
            var req = ValidOutbound();
            req.SenderName = "";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.SenderName);
        }

        [Fact]
        public async Task Outbound_InvalidSenderAddress_Fails()
        {
            var req = ValidOutbound();
            req.SenderAddress = "not-an-email";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.SenderAddress);
        }

        [Fact]
        public async Task Inbound_SkipsSenderRules()
        {
            var req = ValidOutbound();
            req.IsInbound = true;
            req.SenderName = null;
            req.SenderAddress = null;
            var result = await _validator.TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.SenderName);
            result.ShouldNotHaveValidationErrorFor(x => x.SenderAddress);
        }

        [Fact]
        public async Task EmptyUserName_Fails()
        {
            var req = ValidOutbound();
            req.SenderUserName = "";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.SenderUserName);
        }

        [Fact]
        public async Task ShortPassword_Fails()
        {
            var req = ValidOutbound();
            req.AccountPassword = "123";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.AccountPassword);
        }
    }
}
