using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Storage.Validators;
using FluentAssertions;
using Moq;

namespace XUnitTest.Storage
{
    /// <summary>
    /// Unit tests for the Phase 1 upload-security fields on <see cref="StorageConfigurationValidator"/>:
    /// expiry range, positive maximum size, and the allowed/unique access-modifier set.
    /// </summary>
    public class StorageConfigurationValidatorTests
    {
        private readonly Mock<IConfigurationRepository> _configurationRepository = new();

        private StorageConfigurationValidator CreateValidator() => new(_configurationRepository.Object);

        private static SaveStorageConfigurationRequest ValidAzureRequest() => new()
        {
            Name = "azure-config",
            StorageStrategy = "Azure",
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=key;EndpointSuffix=core.windows.net",
        };

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(604_801)]
        public async Task UploadUrlExpirySeconds_OutOfRange_IsRejected(int value)
        {
            var request = ValidAzureRequest();
            request.UploadUrlExpirySeconds = value;

            var result = await CreateValidator().ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.UploadUrlExpirySeconds));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(600)]
        [InlineData(604_800)]
        public async Task UploadUrlExpirySeconds_InRange_IsAccepted(int value)
        {
            var request = ValidAzureRequest();
            request.UploadUrlExpirySeconds = value;

            var result = await CreateValidator().ValidateAsync(request);

            result.Errors.Should().NotContain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.UploadUrlExpirySeconds));
        }

        [Fact]
        public async Task UploadUrlExpirySeconds_Null_IsAccepted()
        {
            var request = ValidAzureRequest();
            request.UploadUrlExpirySeconds = null;

            var result = await CreateValidator().ValidateAsync(request);

            result.Errors.Should().NotContain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.UploadUrlExpirySeconds));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(604_801)]
        public async Task DownloadUrlExpirySeconds_OutOfRange_IsRejected(int value)
        {
            var request = ValidAzureRequest();
            request.DownloadUrlExpirySeconds = value;

            var result = await CreateValidator().ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.DownloadUrlExpirySeconds));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task MaxFileSizeInBytes_NotPositive_IsRejected(long value)
        {
            var request = ValidAzureRequest();
            request.MaxFileSizeInBytes = value;

            var result = await CreateValidator().ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.MaxFileSizeInBytes));
        }

        [Fact]
        public async Task MaxFileSizeInBytes_Positive_IsAccepted()
        {
            var request = ValidAzureRequest();
            request.MaxFileSizeInBytes = 5_242_880;

            var result = await CreateValidator().ValidateAsync(request);

            result.Errors.Should().NotContain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.MaxFileSizeInBytes));
        }

        [Fact]
        public async Task UploadCompletionRequiredFor_Null_IsAccepted()
        {
            var request = ValidAzureRequest();
            request.UploadCompletionRequiredFor = null;

            var result = await CreateValidator().ValidateAsync(request);

            result.Errors.Should().NotContain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.UploadCompletionRequiredFor));
        }

        [Theory]
        [MemberData(nameof(AllowedCombinations))]
        public async Task UploadCompletionRequiredFor_AllowedCombinations_AreAccepted(List<string> accessModifiers)
        {
            var request = ValidAzureRequest();
            request.UploadCompletionRequiredFor = accessModifiers;

            var result = await CreateValidator().ValidateAsync(request);

            result.Errors.Should().NotContain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.UploadCompletionRequiredFor));
        }

        public static TheoryData<List<string>> AllowedCombinations => new()
        {
            new List<string> { "Public" },
            new List<string> { "Private" },
            new List<string> { "Public", "Private" },
        };

        [Fact]
        public async Task UploadCompletionRequiredFor_DisallowedValue_IsRejected()
        {
            var request = ValidAzureRequest();
            request.UploadCompletionRequiredFor = new List<string> { "Secure" };

            var result = await CreateValidator().ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.UploadCompletionRequiredFor));
        }

        [Fact]
        public async Task UploadCompletionRequiredFor_DuplicateValue_IsRejected()
        {
            var request = ValidAzureRequest();
            request.UploadCompletionRequiredFor = new List<string> { "Public", "Public" };

            var result = await CreateValidator().ValidateAsync(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveStorageConfigurationRequest.UploadCompletionRequiredFor));
        }
    }
}
