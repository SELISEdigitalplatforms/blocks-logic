using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using Common.InternalService.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Services
{
    /// <summary>
    /// Unit tests for <see cref="CertificateStorageService"/>. Everything up to the blob write is
    /// covered here; the write itself needs a real storage account, so these pin the guards that
    /// decide whether it happens at all, plus the blob naming contract that decides where it lands.
    /// </summary>
    public class CertificateStorageServiceTests : IDisposable
    {
        private readonly Mock<IConfigurationRepository> _configurationRepository = new();
        private readonly CertificateStorageService _service;

        public CertificateStorageServiceTests()
        {
            TestBlocksContext.Set("tenant-123");
            _service = new CertificateStorageService(_configurationRepository.Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static FormFile CertificateFile(string fileName = "public.pfx", int sizeInBytes = 32)
        {
            var bytes = new byte[sizeInBytes];
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Certificate", fileName);
        }

        private void WithStorageConfiguration(string connectionString)
        {
            _configurationRepository
                .Setup(r => r.GetStorageConfigurationByNameAsync("Default"))
                .ReturnsAsync(new StorageConfiguration { Name = "Default", ConnectionString = connectionString });
        }

        private void WithNoStorageConfiguration()
        {
            _configurationRepository
                .Setup(r => r.GetStorageConfigurationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync((StorageConfiguration)null!);
        }

        // Blob naming

        [Fact]
        public void BuildBlobName_SuffixesTheExternalProvidersCertificate()
        {
            CertificateStorageService.BuildBlobName("tenant-123", isThirdParty: true)
                .Should().Be("tenant-123_3rdparty");
        }

        [Fact]
        public void BuildBlobName_LeavesTheTenantsOwnCertificateOnTheBareTenantId()
        {
            CertificateStorageService.BuildBlobName("tenant-123", isThirdParty: false)
                .Should().Be("tenant-123");
        }

        [Fact]
        public void BuildBlobName_KeepsTheTwoSlotsApart()
        {
            // Overwriting a tenant's own signing certificate with an external one would let that
            // provider mint tokens the tenant's services trust as their own.
            CertificateStorageService.BuildBlobName("tenant-123", true)
                .Should().NotBe(CertificateStorageService.BuildBlobName("tenant-123", false));
        }

        // Tenant context

        [Fact]
        public async Task UploadPublicCertificateAsync_RejectsWhenThereIsNoTenantContext()
        {
            TestBlocksContext.Clear();

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("tenant");
            result.DownloadUrl.Should().BeEmpty();
        }

        // File validation

        [Fact]
        public async Task UploadPublicCertificateAsync_RejectsAMissingFile()
        {
            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = null, IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("certificate");
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_RejectsAnEmptyFile()
        {
            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(sizeInBytes: 0), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("certificate");
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_RejectsAFileOverTwoMegabytes()
        {
            var oversize = CertificateFile(sizeInBytes: (2 * 1024 * 1024) + 1);

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = oversize, IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("certificate");
        }

        [Theory]
        [InlineData("payload.jpg")]
        [InlineData("payload.exe")]
        [InlineData("payload")]
        public async Task UploadPublicCertificateAsync_RejectsFilesThatAreNotCertificates(string fileName)
        {
            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(fileName), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("certificate");
        }

        [Theory]
        [InlineData("public.crt")]
        [InlineData("public.der")]
        [InlineData("public.pfx")]
        [InlineData("public.p12")]
        [InlineData("public.PFX")]
        public async Task UploadPublicCertificateAsync_AcceptsEveryCertificateExtensionTheClientOffers(string fileName)
        {
            // No storage configuration is set up, so a file that clears validation stops at the
            // configuration lookup. Reaching that error is the proof it was not rejected earlier.
            WithNoStorageConfiguration();

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(fileName), IsThirdParty = true });

            result.Errors.Should().ContainKey("configuration");
            result.Errors.Should().NotContainKey("certificate");
        }

        // Storage configuration

        [Fact]
        public async Task UploadPublicCertificateAsync_RejectsWhenNoStorageConfigurationExists()
        {
            WithNoStorageConfiguration();

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("configuration");
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_RejectsAConfigurationWithNoConnectionString()
        {
            WithStorageConfiguration(string.Empty);

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("configuration");
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_ReadsTheDefaultStorageConfiguration()
        {
            WithStorageConfiguration(string.Empty);

            await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            _configurationRepository.Verify(r => r.GetStorageConfigurationByNameAsync("Default"), Times.Once);
        }
    }
}
