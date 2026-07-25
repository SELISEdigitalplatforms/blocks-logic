using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication;
using CloudConfiguration.DomainService.Authentication.Entities;
using CloudConfiguration.DomainService.Captcha.Entities;
using CloudConfiguration.DomainService.MFA.Enums;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.ResponseModel;
using CloudConfiguration.DomainService.IAM.Entities;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.MFA.Entities;
using CloudConfiguration.DomainService.MFA.RequestModel;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using System.Linq.Expressions;
using XUnitTest.TestHelpers;

namespace XUnitTest.CloudConfiguration
{
    public class ConfigurationServiceTests : IDisposable
    {
        private readonly Mock<IConfigurationRepository> _repo = new();
        private readonly Mock<IValidator<SaveCaptchaConfigurationRequest>> _captchaValidator = new();
        private readonly Mock<IValidator<SaveIamConfigurationRequest>> _iamValidator = new();
        private readonly Mock<IValidator<SaveNotificatonConfigurationRequest>> _notificationValidator = new();
        private readonly Mock<IValidator<SaveStorageConfigurationRequest>> _storageValidator = new();
        private readonly Mock<IValidator<MailConfiguration>> _mailValidator = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly ConfigurationService _service;

        public ConfigurationServiceTests()
        {
            TestBlocksContext.Set("tenant-x", "user-x");
            SetValid(_captchaValidator);
            SetValid(_iamValidator);
            SetValid(_notificationValidator);
            SetValid(_storageValidator);
            SetValid(_mailValidator);
            _service = new ConfigurationService(_repo.Object, _captchaValidator.Object, _iamValidator.Object,
                _notificationValidator.Object, _storageValidator.Object, _mailValidator.Object,
                _messageClient.Object, Mock.Of<ILogger<ConfigurationService>>(), _tenants.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private static void SetValid<T>(Mock<IValidator<T>> v) =>
            v.Setup(x => x.ValidateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

        private static void SetInvalid<T>(Mock<IValidator<T>> v) =>
            v.Setup(x => x.ValidateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Field", "invalid") }));

        // ---------- Authentication ----------
        [Fact]
        public async Task UpdateAuthenticationConfig_SavesAndReturnsSuccess()
        {
            var request = new UpdateAuthenticationConfigurationRequest { ItemId = ObjectId.GenerateNewId().ToString() };

            var result = await _service.UpdateAuthenticationConfigAsync(request);

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.UpdateAuthenticationConfigAsync(It.IsAny<AuthenticationConfiguration>()), Times.Once);
        }

        // ---------- Captcha ----------
        [Fact]
        public async Task GetCaptchaConfiguration_NotFound_ReturnsErrorMessage()
        {
            _repo.Setup(r => r.GetCaptchaConfigurationByProviderAsync("prov")).ReturnsAsync((CaptchaConfiguration)null!);

            var result = await _service.GetCaptchaConfigurationAsync("prov");

            result.Configuration.Should().BeNull();
            result.Errors.Should().ContainKey("no_configuration_exist");
        }

        [Fact]
        public async Task GetCaptchaConfiguration_Found_ReturnsConfiguration()
        {
            _repo.Setup(r => r.GetCaptchaConfigurationByProviderAsync("prov"))
                .ReturnsAsync(new CaptchaConfiguration { Provider = "prov" });

            var result = await _service.GetCaptchaConfigurationAsync("prov");

            result.Configuration.Should().NotBeNull();
        }

        [Fact]
        public async Task SaveCaptchaConfiguration_Invalid_ReturnsError()
        {
            SetInvalid(_captchaValidator);
            var result = await _service.SaveCaptchaConfigurationAsync(new SaveCaptchaConfigurationRequest { Provider = "p" });

            result.IsSuccess.Should().BeFalse();
            _repo.Verify(r => r.SaveCaptchaConfigurationAsync(It.IsAny<CaptchaConfiguration>()), Times.Never);
        }

        [Fact]
        public async Task SaveCaptchaConfiguration_Valid_NewConfig_Saves()
        {
            _repo.Setup(r => r.GetCaptchaConfigurationByProviderAsync(It.IsAny<string>())).ReturnsAsync((CaptchaConfiguration)null!);

            var result = await _service.SaveCaptchaConfigurationAsync(new SaveCaptchaConfigurationRequest { Provider = "p" });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveCaptchaConfigurationAsync(It.IsAny<CaptchaConfiguration>()), Times.Once);
        }

        [Fact]
        public async Task UpdateCaptchaConfigurationStatus_ReturnsSuccess()
        {
            var result = await _service.UpdateCaptchaConfigurationStatusAsync(new UpdateCaptchaConfigurationStatusRequest());
            result.IsSuccess.Should().BeTrue();
        }

        // ---------- IAM ----------
        [Fact]
        public async Task SaveIamConfiguration_Invalid_ReturnsError()
        {
            SetInvalid(_iamValidator);
            var result = await _service.SaveIamConfigurationAsync(new SaveIamConfigurationRequest());
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task SaveIamConfiguration_Valid_Saves()
        {
            _repo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync((IamConfiguration)null!);
            var result = await _service.SaveIamConfigurationAsync(new SaveIamConfigurationRequest());
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveIamConfigurationAsync(It.IsAny<IamConfiguration>()), Times.Once);
        }

        [Fact]
        public async Task GetIamConfiguration_WrapsData()
        {
            var config = new IamConfiguration();
            _repo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(config);
            var result = await _service.GetIamConfigurationAsync();
            result.Data.Should().BeSameAs(config);
        }

        // ---------- MFA ----------
        [Fact]
        public async Task SaveMfaConfiguration_NewConfig_SavesAndPublishes()
        {
            _repo.Setup(r => r.GetDefaultMfaConfiguration()).ReturnsAsync((MfaConfiguration)null!);

            var result = await _service.SaveMfaConfigurationAsync(new SaveMfaConfigurationRequest
            {
                EnableMfa = true,
                UserMfaType = new List<CloudConfigurationUserMfaType>()
            });

            result.IsSuccess.Should().BeTrue();
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MfaActionEvent>>()), Times.Once);
        }

        [Fact]
        public async Task GetMfaConfiguration_Null_ReturnsDefaults()
        {
            _repo.Setup(r => r.GetDefaultMfaConfiguration()).ReturnsAsync((MfaConfiguration)null!);
            var result = await _service.GetMfaConfigurationAsync();
            result.MfaTemplate.Should().NotBeNull();
        }

        [Fact]
        public async Task GetMfaConfiguration_Existing_ReturnsValues()
        {
            _repo.Setup(r => r.GetDefaultMfaConfiguration())
                .ReturnsAsync(new MfaConfiguration { EnableMfa = true, MfaTemplate = new MfaTemplate(), UserMfaTypes = new() });
            var result = await _service.GetMfaConfigurationAsync();
            result.EnableMfa.Should().BeTrue();
        }

        // ---------- Notification ----------
        [Fact]
        public async Task SaveNotificationConfiguration_Invalid_ReturnsError()
        {
            SetInvalid(_notificationValidator);
            var result = await _service.SaveNotificationConfigurationAsync(new SaveNotificatonConfigurationRequest());
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task SaveNotificationConfiguration_Valid_Saves()
        {
            _repo.Setup(r => r.GetNotificationConfigurationByNameAsync(It.IsAny<string>())).ReturnsAsync((NotificationConfiguration)null!);
            var result = await _service.SaveNotificationConfigurationAsync(new SaveNotificatonConfigurationRequest { Name = "n" });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveNotificationConfigurationAsync(It.IsAny<NotificationConfiguration>()), Times.Once);
        }

        [Fact]
        public async Task GetNotificatoinConfiguration_ReturnsFromRepo()
        {
            var config = new NotificationConfiguration();
            _repo.Setup(r => r.GetNotificationConfigurationByIdAsync(It.IsAny<string>())).ReturnsAsync(config);
            var result = await _service.GetNotificatoinConfigurationAsync(new GetNotificationConfigurationRequest { ItemId = "id" });
            result.Should().BeSameAs(config);
        }

        // ---------- Storage ----------
        [Fact]
        public async Task SaveStorageConfiguration_Invalid_ReturnsError()
        {
            SetInvalid(_storageValidator);
            var result = await _service.SaveStorageConfigurationAsync(new SaveStorageConfigurationRequest { Name = "n", StorageStrategy = "Azure" });
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task SaveStorageConfiguration_Valid_SavesAndPublishes()
        {
            _repo.Setup(r => r.GetStorageConfigurationByNameAsync(It.IsAny<string>())).ReturnsAsync((StorageConfiguration)null!);

            var result = await _service.SaveStorageConfigurationAsync(new SaveStorageConfigurationRequest { Name = "n", StorageStrategy = "Azure" });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveStorageConfigurationAsync(It.IsAny<StorageConfiguration>()), Times.Once);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateDefaultFolderEvent>>()), Times.Once);
        }

        [Fact]
        public async Task GetStorageConfigurations_MasksSecrets()
        {
            _repo.Setup(r => r.GetAllStorageConfigurationsByDateAsync()).ReturnsAsync(new List<StorageConfiguration>
            {
                new() { StorageStrategy = "SftpStorage", Password = "secret", SftpSecretKey = "k" },
                new() { StorageStrategy = "Azure", ConnectionString = "DefaultEndpointsProtocol=https;AccountName=a;AccountKey=k;EndpointSuffix=core" }
            });

            var result = await _service.GetStorageConfigurationsAsync();

            result[0].Password.Should().Be("********");
            result[0].SftpSecretKey.Should().Be("********");
        }

        [Fact]
        public async Task GetStorageConfiguration_Sftp_MasksPassword()
        {
            _repo.Setup(r => r.GetStorageConfigurationByNameAsync("n"))
                .ReturnsAsync(new StorageConfiguration { StorageStrategy = "SftpStorage", Password = "secret", SftpSecretKey = "k" });

            var result = await _service.GetStorageConfigurationAsync("n");

            result.Password.Should().Be("********");
        }

        [Fact]
        public async Task DeleteStorageConfiguration_ReturnsSuccess()
        {
            var result = await _service.DeleteStorageConfigurationAsync("n");
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteStorageConfigurationByNameAsync("n"), Times.Once);
        }

        // ---------- Mail ----------
        [Fact]
        public async Task SaveMailConfiguration_Invalid_ReturnsError()
        {
            SetInvalid(_mailValidator);
            var result = await _service.SaveMailConfigurationAsync(new MailConfiguration());
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task SaveMailConfiguration_Valid_Saves()
        {
            _repo.Setup(r => r.GetMailConfigurationByIdAsync(It.IsAny<string>())).ReturnsAsync((MailServerConfiguration)null!);
            var result = await _service.SaveMailConfigurationAsync(new MailConfiguration { ConfigurationId = "id" });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveMailConfigurationAsync(It.IsAny<MailServerConfiguration>()), Times.Once);
        }

        [Fact]
        public async Task GetMailConfiguration_MasksPassword()
        {
            _repo.Setup(r => r.GetMailConfigurationByNameAsync("n"))
                .ReturnsAsync(new MailConfiguration { AccountPassword = "secret" });
            var result = await _service.GetMailConfigurationAsync(new GetMailConfigurationRequest { ConfigurationName = "n" });
            result.AccountPassword.Should().Be("********");
        }

        [Fact]
        public async Task GetAllMailConfigurations_MasksPasswords()
        {
            _repo.Setup(r => r.GetAllMailConfigurationsAsync())
                .ReturnsAsync(new List<MailServerConfiguration> { new() { AccountPassword = "secret" } });
            var result = await _service.GetAllMailConfigurationsAsync();
            result[0].AccountPassword.Should().Be("********");
        }

        [Fact]
        public async Task DeleteMailConfiguration_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetMailConfigurationByIdAsync(It.IsAny<string>())).ReturnsAsync((MailServerConfiguration)null!);
            var result = await _service.DeleteMailConfigurationAsync(new DeleteMailConfigurationRequest { ConfigurationId = "id" });
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task DeleteMailConfiguration_Found_Deletes()
        {
            _repo.Setup(r => r.GetMailConfigurationByIdAsync(It.IsAny<string>())).ReturnsAsync(new MailServerConfiguration());
            var result = await _service.DeleteMailConfigurationAsync(new DeleteMailConfigurationRequest { ConfigurationId = "id" });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.DeleteMailConfigurationAsync("id"), Times.Once);
        }

        [Fact]
        public async Task DuplicateMailConfiguration_NotFound_ReturnsError()
        {
            _repo.Setup(r => r.GetMailConfigurationByIdAsync(It.IsAny<string>())).ReturnsAsync((MailServerConfiguration)null!);
            var result = await _service.DuplicateMailConfigurationAsync(new DuplicateMailConfigurationRequest { ConfigurationId = "id" });
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task DuplicateMailConfiguration_Found_SavesCopy()
        {
            _repo.Setup(r => r.GetMailConfigurationByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new MailServerConfiguration { Name = "Primary" });
            var result = await _service.DuplicateMailConfigurationAsync(new DuplicateMailConfigurationRequest { ConfigurationId = "id" });
            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveMailConfigurationAsync(It.Is<MailServerConfiguration>(c => c.Name == "Primary - Copy")), Times.Once);
        }
    }
}
