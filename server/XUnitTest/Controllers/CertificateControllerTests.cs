using BlocksTemplate.Api.Controllers;
using Common.InternalService.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// Unit tests for <see cref="CertificateController"/>. The controller is a thin pass-through
    /// over <see cref="ICertificateStorageService"/>, so what is worth pinning is that it composes
    /// the request from the two different binding sources the client actually uses - the file from
    /// the multipart body, the flag from the query string - and returns the service's own result.
    /// </summary>
    public class CertificateControllerTests
    {
        private readonly Mock<ICertificateStorageService> _service = new();
        private readonly CertificateController _controller;

        public CertificateControllerTests()
        {
            _controller = new CertificateController(_service.Object);
        }

        private static FormFile CertificateFile(string fileName = "public.pfx")
        {
            var bytes = "certificate-bytes"u8.ToArray();
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Certificate", fileName);
        }

        [Fact]
        public async Task UploadCertificate_HandsTheFileAndFlagToTheService()
        {
            var file = CertificateFile();
            _service.Setup(s => s.UploadPublicCertificateAsync(It.IsAny<UploadCertificateRequest>()))
                    .ReturnsAsync(new UploadCertificateResponse { IsSuccess = true });

            await _controller.UploadCertificate(file, isThirdParty: true);

            _service.Verify(s => s.UploadPublicCertificateAsync(
                It.Is<UploadCertificateRequest>(r => r.Certificate == file && r.IsThirdParty)), Times.Once);
        }

        [Fact]
        public async Task UploadCertificate_CarriesAFalseThirdPartyFlagThrough()
        {
            _service.Setup(s => s.UploadPublicCertificateAsync(It.IsAny<UploadCertificateRequest>()))
                    .ReturnsAsync(new UploadCertificateResponse { IsSuccess = true });

            await _controller.UploadCertificate(CertificateFile(), isThirdParty: false);

            _service.Verify(s => s.UploadPublicCertificateAsync(
                It.Is<UploadCertificateRequest>(r => !r.IsThirdParty)), Times.Once);
        }

        [Fact]
        public async Task UploadCertificate_ReturnsTheServiceResponseUnchanged()
        {
            var expected = new UploadCertificateResponse
            {
                IsSuccess = true,
                DownloadUrl = "https://account.blob.core.windows.net/certificates/tenant-123_3rdparty"
            };
            _service.Setup(s => s.UploadPublicCertificateAsync(It.IsAny<UploadCertificateRequest>()))
                    .ReturnsAsync(expected);

            var result = await _controller.UploadCertificate(CertificateFile(), isThirdParty: true);

            result.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task UploadCertificate_LetsTheServiceRejectAMissingFile()
        {
            // The client can post the form without the file part; binding yields null rather than
            // failing, so the rejection has to come from the service, not the controller.
            _service.Setup(s => s.UploadPublicCertificateAsync(It.IsAny<UploadCertificateRequest>()))
                    .ReturnsAsync(new UploadCertificateResponse { IsSuccess = false });

            var result = await _controller.UploadCertificate(certificate: null, isThirdParty: true);

            result.IsSuccess.Should().BeFalse();
            _service.Verify(s => s.UploadPublicCertificateAsync(
                It.Is<UploadCertificateRequest>(r => r.Certificate == null)), Times.Once);
        }
    }
}
