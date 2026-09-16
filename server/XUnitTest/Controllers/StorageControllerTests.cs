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
