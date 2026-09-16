using Blocks.Genesis;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Storage
{
    /// <summary>
    /// Unit tests for <see cref="ConfigurationService"/>'s storage-configuration save path. The
    /// repository and validators are mocked so these tests assert the mapping from
    /// <see cref="SaveStorageConfigurationRequest"/> onto the persisted <see cref="StorageConfiguration"/>,
    /// in particular the Phase 1 upload-security fields (P1.14 persistence-mapping coverage).
    /// </summary>
    public class StorageConfigurationServiceTests : IDisposable
    {
        private readonly Mock<IConfigurationRepository> _repository = new();
        private readonly ConfigurationService _sut;

        public StorageConfigurationServiceTests()
        {
            var storageValidator = new Mock<IValidator<SaveStorageConfigurationRequest>>();
            storageValidator
                .Setup(v => v.ValidateAsync(It.IsAny<SaveStorageConfigurationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var messageClient = new Mock<IMessageClient>();
            messageClient
                .Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<CreateDefaultFolderEvent>>()))
                .Returns(Task.CompletedTask);

            TestBlocksContext.Set("tenant-storage", "user-storage");

            _sut = new ConfigurationService(
                _repository.Object,
                Mock.Of<IValidator<SaveCaptchaConfigurationRequest>>(),
                Mock.Of<IValidator<SaveIamConfigurationRequest>>(),
                Mock.Of<IValidator<SaveNotificatonConfigurationRequest>>(),
                storageValidator.Object,
                Mock.Of<IValidator<MailConfiguration>>(),
                messageClient.Object,
                Mock.Of<ILogger<ConfigurationService>>(),
                Mock.Of<ITenants>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static SaveStorageConfigurationRequest SaveRequest() => new()
        {
            Name = "azure-config",
            StorageStrategy = "Azure",
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=key;EndpointSuffix=core.windows.net",
            UploadUrlExpirySeconds = 900,
            DownloadUrlExpirySeconds = 120,
            MaxFileSizeInBytes = 10_485_760,
            UploadCompletionRequiredFor = new List<string> { "Public", "Private" },
        };

        [Fact]
        public async Task SaveStorageConfigurationAsync_NewConfiguration_PersistsThePhase1UploadSecurityFields()
        {
            _repository.Setup(r => r.GetStorageConfigurationByNameAsync(It.IsAny<string>()))
                       .ReturnsAsync((StorageConfiguration)null!);

            StorageConfiguration? stored = null;
            _repository.Setup(r => r.SaveStorageConfigurationAsync(It.IsAny<StorageConfiguration>()))
                       .Callback<StorageConfiguration>(c => stored = c)
                       .Returns(Task.CompletedTask);

            var result = await _sut.SaveStorageConfigurationAsync(SaveRequest());

            result.IsSuccess.Should().BeTrue();
            stored.Should().NotBeNull();
            stored!.UploadUrlExpirySeconds.Should().Be(900);
            stored.DownloadUrlExpirySeconds.Should().Be(120);
            stored.MaxFileSizeInBytes.Should().Be(10_485_760);
            stored.UploadCompletionRequiredFor.Should().BeEquivalentTo(new List<string> { "Public", "Private" });
        }

        [Fact]
        public async Task SaveStorageConfigurationAsync_UpdateOmittingTheFields_ClearsThemRatherThanKeepingTheStoredValues()
        {
            var existing = new StorageConfiguration
            {
                ItemId = "existing-id",
                Name = "azure-config",
                UploadUrlExpirySeconds = 900,
                DownloadUrlExpirySeconds = 120,
                MaxFileSizeInBytes = 10_485_760,
                UploadCompletionRequiredFor = new List<string> { "Public" },
            };
            _repository.Setup(r => r.GetStorageConfigurationByIdAsync("existing-id")).ReturnsAsync(existing);
            _repository.Setup(r => r.GetStorageConfigurationByNameAsync(It.IsAny<string>()))
                       .ReturnsAsync((StorageConfiguration)null!);

            StorageConfiguration? stored = null;
            _repository.Setup(r => r.SaveStorageConfigurationAsync(It.IsAny<StorageConfiguration>()))
                       .Callback<StorageConfiguration>(c => stored = c)
                       .Returns(Task.CompletedTask);

            var request = SaveRequest();
            request.UpdateRequest = true;
            request.ItemId = "existing-id";
            request.UploadUrlExpirySeconds = null;
            request.DownloadUrlExpirySeconds = null;
            request.MaxFileSizeInBytes = null;
            request.UploadCompletionRequiredFor = null;

            var result = await _sut.SaveStorageConfigurationAsync(request);

            result.IsSuccess.Should().BeTrue();
            stored.Should().NotBeNull();
            stored!.UploadUrlExpirySeconds.Should().BeNull();
            stored.DownloadUrlExpirySeconds.Should().BeNull();
            stored.MaxFileSizeInBytes.Should().BeNull();
            stored.UploadCompletionRequiredFor.Should().BeNull();
        }

        [Fact]
        public async Task SaveStorageConfigurationAsync_ValidationFails_DoesNotPersist()
        {
            var storageValidator = new Mock<IValidator<SaveStorageConfigurationRequest>>();
            storageValidator
                .Setup(v => v.ValidateAsync(It.IsAny<SaveStorageConfigurationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("MaxFileSizeInBytes", "must be positive") }));

            var sut = new ConfigurationService(
                _repository.Object,
                Mock.Of<IValidator<SaveCaptchaConfigurationRequest>>(),
                Mock.Of<IValidator<SaveIamConfigurationRequest>>(),
                Mock.Of<IValidator<SaveNotificatonConfigurationRequest>>(),
                storageValidator.Object,
                Mock.Of<IValidator<MailConfiguration>>(),
                Mock.Of<IMessageClient>(),
                Mock.Of<ILogger<ConfigurationService>>(),
                Mock.Of<ITenants>());

            var result = await sut.SaveStorageConfigurationAsync(SaveRequest());

            result.IsSuccess.Should().BeFalse();
            _repository.Verify(r => r.SaveStorageConfigurationAsync(It.IsAny<StorageConfiguration>()), Times.Never);
        }
    }
}
