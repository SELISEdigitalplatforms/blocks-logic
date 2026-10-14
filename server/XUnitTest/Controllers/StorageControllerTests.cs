using System.Text.Json;
using System.Text.Json.Nodes;
using Blocks.Genesis;
using StorageConfiguration = Blocks.Genesis.Entities.Data.StorageConfiguration;
using PackageAccessModifier = Blocks.Genesis.Entities.Data.AccessModifier;
using BlocksTemplate.Api.Controllers;
using CloudConfiguration.DomainService.Shared.Services;
using DomainService.Storage;
using FluentAssertions;
using Moq;
using Storage.DomainService.Enums;
using StorageDriver;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// Unit tests for <see cref="StorageController"/>'s pure pass-through actions. P1.7's Logic
    /// compatibility wrapper and P1.9's Logic compatibility route both just forward to
    /// <see cref="IStorageDriverService"/> (which delegates to blocks-data's own verification
    /// pipeline) without duplicating any logic, so these tests only need to prove the exact object
    /// the driver returns reaches the caller unchanged - including the Phase 1 fields the driver
    /// added after this controller's routes were first written.
    /// </summary>
    public class StorageControllerTests
    {
        private readonly Mock<IConfigurationService> _configurationService = new();
        private readonly Mock<IStorageDriverService> _storageDriverService = new();
        private readonly StorageController _controller;

        public StorageControllerTests()
        {
            _controller = new StorageController(_configurationService.Object, _storageDriverService.Object);
        }

        // Today's Storage API shape: the old logic StorageConfiguration (BaseEntity + these fields, upload
        // completion as List<string>). Kept here as the reference so the JSON can be compared byte for byte.
        private sealed class LegacyStorageConfigurationShape : BaseEntity
        {
            public string Name { get; set; } = "";
            public string ConnectionString { get; set; } = "";
            public string SecretKey { get; set; } = "";
            public string AccessKey { get; set; } = "";
            public string StorageStrategy { get; set; } = "";
            public string CloudStorageRegionEndPoint { get; set; } = "";
            public string Host { get; set; } = "";
            public string Port { get; set; } = "";
            public string UserName { get; set; } = "";
            public string Password { get; set; } = "";
            public string RemoteBasePath { get; set; } = "";
            public string SftpSecretKey { get; set; } = "";
            public int? UploadUrlExpirySeconds { get; set; }
            public int? DownloadUrlExpirySeconds { get; set; }
            public long? MaxFileSizeInBytes { get; set; }
            public List<string> UploadCompletionRequiredFor { get; set; } = null!;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GetsAndGet_SerializeUploadCompletionRequiredForAsStrings_SameJsonAsBefore(bool withList)
        {
            var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var stored = new StorageConfiguration
            {
                ItemId = "id-1", CreatedDate = created, LastUpdatedDate = created, CreatedBy = "u", LastUpdatedBy = "u",
                Name = "azure", ConnectionString = "c", SecretKey = "s", AccessKey = "a", StorageStrategy = "Azure",
                CloudStorageRegionEndPoint = "e", Host = "h", Port = "22", UserName = "n", Password = "p",
                RemoteBasePath = "/r", SftpSecretKey = "k", UploadUrlExpirySeconds = 900, DownloadUrlExpirySeconds = 120,
                MaxFileSizeInBytes = 1024,
                UploadCompletionRequiredFor = withList ? [PackageAccessModifier.Public, PackageAccessModifier.Private] : null,
            };
            var legacy = new LegacyStorageConfigurationShape
            {
                ItemId = "id-1", CreatedDate = created, LastUpdatedDate = created, CreatedBy = "u", LastUpdatedBy = "u",
                Name = "azure", ConnectionString = "c", SecretKey = "s", AccessKey = "a", StorageStrategy = "Azure",
                CloudStorageRegionEndPoint = "e", Host = "h", Port = "22", UserName = "n", Password = "p",
                RemoteBasePath = "/r", SftpSecretKey = "k", UploadUrlExpirySeconds = 900, DownloadUrlExpirySeconds = 120,
                MaxFileSizeInBytes = 1024,
                UploadCompletionRequiredFor = withList ? ["Public", "Private"] : null!,
            };
            _configurationService.Setup(s => s.GetStorageConfigurationsAsync()).ReturnsAsync([stored]);
            _configurationService.Setup(s => s.GetStorageConfigurationAsync("azure")).ReturnsAsync(stored);

            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var expected = JsonSerializer.Serialize(legacy, web);

            var list = await _controller.Gets(new());
            var one = await _controller.Get(new() { ConfigurationName = "azure" });

            JsonSerializer.Serialize(one, web).Should().Be(expected);
            JsonSerializer.Serialize(list, web).Should().Be("[" + expected + "]");
            if (withList)
                JsonNode.Parse(expected)!["uploadCompletionRequiredFor"]!.ToJsonString().Should().Be("[\"Public\",\"Private\"]");
        }

        [Fact]
        public async Task GetPreSignedUrlForUpload_ReturnsTheExpandedDriverResponseUnchanged()
        {
            var request = new GetPreSignedUrlForUploadRequest { Name = "report.pdf" };
            var driverResponse = new GetPreSignedUrlForUploadResponse
            {
                IsSuccess = true,
                FileId = "file-1",
                UploadUrl = "https://provider/upload?sig=abc",
                FileVersionId = "version-1",
                UploadUrlExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                RequiredHeaders = new Dictionary<string, string> { ["x-ms-blob-type"] = "BlockBlob" },
                UploadCompletionRequired = true,
                VerificationStatus = FileVerificationStatus.Quarantined,
            };
            _storageDriverService.Setup(s => s.GetPerSignedUrlForUploadAsync(request)).ReturnsAsync(driverResponse);

            var result = await _controller.GetPreSignedUrlForUpload(request);

            result.Should().BeSameAs(driverResponse);
            result.FileVersionId.Should().Be("version-1");
            result.UploadCompletionRequired.Should().BeTrue();
            result.RequiredHeaders.Should().ContainKey("x-ms-blob-type");
            result.VerificationStatus.Should().Be(FileVerificationStatus.Quarantined);
        }

        [Fact]
        public async Task CompleteUpload_DelegatesToTheStorageDriverAndReturnsItsResponseUnchanged()
        {
            var request = new CompleteUploadRequest { FileId = "file-1", FileVersionId = "version-1" };
            var driverResponse = new CompleteUploadResponse
            {
                IsSuccess = true,
                FileId = "file-1",
                FileVersionId = "version-1",
                VerificationStatus = FileVerificationStatus.Verified,
            };
            _storageDriverService.Setup(s => s.CompleteUploadAsync(request)).ReturnsAsync(driverResponse);

            var result = await _controller.CompleteUpload(request);

            result.Should().BeSameAs(driverResponse);
            _storageDriverService.Verify(s => s.CompleteUploadAsync(request), Times.Once);
        }

        [Fact]
        public async Task CompleteUpload_RejectionFromTheDriver_IsForwardedUnchanged()
        {
            var request = new CompleteUploadRequest { FileId = "file-1", FileVersionId = "version-1" };
            var driverResponse = new CompleteUploadResponse
            {
                IsSuccess = true,
                FileId = "file-1",
                FileVersionId = "version-1",
                VerificationStatus = FileVerificationStatus.Rejected,
                RejectionReason = "size_exceeds_declared",
            };
            _storageDriverService.Setup(s => s.CompleteUploadAsync(request)).ReturnsAsync(driverResponse);

            var result = await _controller.CompleteUpload(request);

            result.VerificationStatus.Should().Be(FileVerificationStatus.Rejected);
            result.RejectionReason.Should().Be("size_exceeds_declared");
        }
    }
}
