using Amazon.S3;
using Azure;
using Common.InternalService.Storage;
using DomainService.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using XUnitTest.TestHelpers;
using Blocks.Genesis.Entities.Data;

namespace XUnitTest.Services
{
    /// <summary>
    /// Unit tests for <see cref="CertificateStorageService"/>. The storage service is mocked, so the guards
    /// that decide whether a write happens, the blob naming contract that decides where it lands and the
    /// request the service makes (container, key, content type) are covered here. Whether a store serves
    /// the object anonymously needs a real store: the provider tests (<see cref="PublicObjectStorageTests"/>)
    /// cover each provider's calls, and the I02-WP9 run covers Azurite and S3.
    /// </summary>
    public class CertificateStorageServiceTests : IDisposable
    {
        private readonly Mock<IStorageConfigurationProvider> _storageConfigurations = new();
        private readonly Mock<IStorageServiceFactory> _factory = new();
        private readonly Mock<IStorageService> _storage = new();
        private readonly CertificateStorageService _service;

        public CertificateStorageServiceTests()
        {
            TestBlocksContext.Set("tenant-123");
            _factory.Setup(f => f.GetStorageService(It.IsAny<StorageConfiguration>())).Returns(_storage.Object);
            _service = new CertificateStorageService(_storageConfigurations.Object, _factory.Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static FormFile CertificateFile(string fileName = "public.pfx", int sizeInBytes = 32)
        {
            var bytes = new byte[sizeInBytes];
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Certificate", fileName) { Headers = new HeaderDictionary() };
        }

        private void WithStorageConfiguration(string strategy = "azure")
        {
            _storageConfigurations
                .Setup(r => r.GetByNameAsync("Default", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StorageConfiguration { Name = "Default", StorageStrategy = strategy });
        }

        private readonly List<(string Container, string Key, long Length, string ContentType)> _uploads = new();

        private void WithUploadReturning(string url)
        {
            WithStorageConfiguration();
            _storage
                .Setup(s => s.UploadPublicObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
                .Callback<string, string, Stream, string>((container, key, content, contentType) =>
                    _uploads.Add((container, key, content.Length, contentType)))
                .ReturnsAsync(url);
        }

        private void WithUploadThrowing(Exception exception)
        {
            WithStorageConfiguration();
            _storage
                .Setup(s => s.UploadPublicObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
                .ThrowsAsync(exception);
        }

        private void WithNoStorageConfiguration()
        {
            _storageConfigurations
                .Setup(r => r.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StorageConfiguration?)null);
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
        public async Task UploadPublicCertificateAsync_RejectsAConfigurationWhoseStrategyIsUnknown()
        {
            WithStorageConfiguration(string.Empty);
            _factory.Setup(f => f.GetStorageService(It.IsAny<StorageConfiguration>()))
                    .Throws(new ArgumentException("Invalid storage provider", "unknown"));

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("configuration");
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_ReadsTheDefaultStorageConfiguration()
        {
            WithUploadReturning("http://s3:8333/certificates/tenant-123_3rdparty");

            await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            _storageConfigurations.Verify(r => r.GetByNameAsync("Default", It.IsAny<CancellationToken>()), Times.Once);
        }

        // The upload

        [Fact]
        public async Task UploadPublicCertificateAsync_StoresInTheSharedCertificatesContainerAndReturnsTheUrl()
        {
            WithUploadReturning("http://s3:8333/certificates/tenant-123_3rdparty");

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(sizeInBytes: 64), IsThirdParty = true });

            result.IsSuccess.Should().BeTrue();
            result.DownloadUrl.Should().Be("http://s3:8333/certificates/tenant-123_3rdparty");
            var upload = _uploads.Should().ContainSingle().Subject;
            upload.Container.Should().Be("certificates");
            upload.Key.Should().Be("tenant-123_3rdparty");
            upload.Length.Should().Be(64);
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_UsesTheBareTenantSlotWhenNotThirdParty()
        {
            WithUploadReturning("u");

            await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = false });

            _uploads.Should().ContainSingle().Which.Key.Should().Be("tenant-123");
        }

        [Theory]
        [InlineData("application/x-pkcs12", "application/x-pkcs12")]
        [InlineData("", "application/octet-stream")]
        public async Task UploadPublicCertificateAsync_CarriesTheContentTypeWithAFallback(string sent, string expected)
        {
            WithUploadReturning("u");
            var file = CertificateFile();
            file.ContentType = sent;

            await _service.UploadPublicCertificateAsync(new UploadCertificateRequest { Certificate = file, IsThirdParty = true });

            _uploads.Should().ContainSingle().Which.ContentType.Should().Be(expected);
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_SurfacesTheProvidersErrorCode()
        {
            WithUploadThrowing(new RequestFailedException(403, "no", "PublicAccessNotPermitted", null));

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("storage").WhoseValue.Should().Be("PublicAccessNotPermitted");
            result.DownloadUrl.Should().BeEmpty();
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_SurfacesAnS3ErrorCode()
        {
            WithUploadThrowing(new AmazonS3Exception("denied", Amazon.Runtime.ErrorType.Sender, "AccessDenied", "r1", System.Net.HttpStatusCode.Forbidden));

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            result.Errors.Should().ContainKey("storage").WhoseValue.Should().Be("AccessDenied");
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_SaysClearlyWhenTheStorageHasNoPublicRead()
        {
            WithUploadThrowing(new NotSupportedException("sftp"));

            var result = await _service.UploadPublicCertificateAsync(
                new UploadCertificateRequest { Certificate = CertificateFile(), IsThirdParty = true });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("storage").WhoseValue.Should().Be("public_objects_not_supported");
        }
    }
}
