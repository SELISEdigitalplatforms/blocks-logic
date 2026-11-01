using System.Net;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Data;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StorageDriver;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    /// <summary>SPEC8 file upload: identity, the pre-signed upload path and how storage answers map to statuses.</summary>
    public class StorageWebhookFileStoreTests : IDisposable
    {
        private readonly Mock<IStorageDriverService> _driver = new();
        private readonly RecordingHandler _provider = new();
        private readonly StorageWebhookFileStore _sut;

        public StorageWebhookFileStoreTests()
        {
            TestBlocksContext.Set("tenant-1", "ambient-user");
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(StorageWebhookFileStore.HttpClientName)).Returns(() => new HttpClient(_provider));
            _sut = new StorageWebhookFileStore(
                factory.Object,
                Options.Create(new WebhookUploadOptions { SystemUserId = "svc-webhook" }),
                NullLogger<StorageWebhookFileStore>.Instance,
                _driver.Object);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private static async Task<WebhookPendingFile> PdfAsync()
        {
            var content = await WebhookContentReader.ReadAnyContentAsync(
                WebhookRequests.Create("application/pdf", new byte[] { 1, 2, 3 }),
                new WebhookIntakeSettings { BodyMode = WebhookBodyModes.AnyContent });
            return content.Files.Single();
        }

        private static WebhookFileTarget Anonymous => new() { TenantId = "tenant-1", FolderId = "dir-1", ConfigurationName = "wf-50" };

        private void SessionReturns(GetPreSignedUrlForUploadResponse response) =>
            _driver.Setup(d => d.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>())).ReturnsAsync(response);

        private static GetPreSignedUrlForUploadResponse Session(bool completion = false) => new()
        {
            IsSuccess = true,
            FileId = "file-1",
            FileVersionId = "v-1",
            UploadUrl = "https://storage.example/upload",
            UploadCompletionRequired = completion,
            RequiredHeaders = new Dictionary<string, string> { ["x-ms-blob-type"] = "BlockBlob" },
        };

        [Fact]
        public async Task Uploads_the_bytes_to_the_pre_signed_url_and_returns_the_file_id()
        {
            GetPreSignedUrlForUploadRequest? sent = null;
            _driver.Setup(d => d.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .Callback<GetPreSignedUrlForUploadRequest>(r => sent = r)
                .ReturnsAsync(Session());
            using var file = await PdfAsync();

            var fileId = await _sut.StoreAsync(file, Anonymous);

            fileId.Should().Be("file-1");
            sent!.Name.Should().Be("upload");
            sent.ParentDirectoryId.Should().Be("dir-1");
            sent.ConfigurationName.Should().Be("wf-50");
            sent.AccessModifier.Should().Be("Private");
            sent.SizeInBytes.Should().Be(3);
            sent.ContentType.Should().Be("application/pdf");
            _provider.Method.Should().Be(HttpMethod.Put);
            _provider.Body.Should().Equal(1, 2, 3);
            _provider.Headers.Should().Contain("x-ms-blob-type");
        }

        [Fact]
        public async Task Anonymous_webhook_uploads_as_the_service_principal_with_organization_access()   // H9, A6
        {
            BlocksContext? acting = null;
            GetPreSignedUrlForUploadRequest? sent = null;
            _driver.Setup(d => d.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .Callback<GetPreSignedUrlForUploadRequest>(r => { acting = BlocksContext.GetContext(); sent = r; })
                .ReturnsAsync(Session());
            using var file = await PdfAsync();

            await _sut.StoreAsync(file, Anonymous);

            acting!.UserId.Should().Be("svc-webhook");
            acting.TenantId.Should().Be("tenant-1");
            sent!.ObjectAccessLevel.Should().Be("Organization");
            BlocksContext.GetContext()!.UserId.Should().Be("ambient-user", "the ambient context is restored");
        }

        [Fact]
        public async Task Authenticated_caller_uploads_as_themselves_with_creator_access()   // H9
        {
            BlocksContext? acting = null;
            GetPreSignedUrlForUploadRequest? sent = null;
            _driver.Setup(d => d.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .Callback<GetPreSignedUrlForUploadRequest>(r => { acting = BlocksContext.GetContext(); sent = r; })
                .ReturnsAsync(Session());
            using var file = await PdfAsync();
            var target = new WebhookFileTarget { TenantId = "tenant-1", Caller = TestBlocksContext.Create("tenant-1", "user-7") };

            await _sut.StoreAsync(file, target);

            acting!.UserId.Should().Be("user-7");
            sent!.ObjectAccessLevel.Should().Be("Creator");
        }

        [Theory]
        [InlineData("declared_size_exceeds_maximum_allowed", 413, "File 'upload' exceeds the storage limit")]
        [InlineData("forbidden", 403, "Not allowed to store files in the configured folder")]
        [InlineData("something_else", 422, "File 'upload' was rejected by storage")]
        public async Task Refused_upload_session_maps_to_a_status(string error, int status, string message)   // C6, A6
        {
            var key = error == "forbidden" ? "access" : "SizeInBytes";
            SessionReturns(new GetPreSignedUrlForUploadResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { [key] = error },
            });
            using var file = await PdfAsync();

            var act = () => _sut.StoreAsync(file, Anonymous);

            var thrown = (await act.Should().ThrowAsync<WebhookIntakeException>()).Which;
            (thrown.StatusCode, thrown.Message).Should().Be((status, message));
            _provider.Method.Should().BeNull("nothing is uploaded after a refusal");
        }

        [Theory]
        [InlineData(FileVerificationStatus.Rejected, "content_type_mismatch", 422)]
        [InlineData(FileVerificationStatus.Rejected, "actual_size_exceeds_maximum_allowed", 413)]
        public async Task Rejected_verification_maps_to_a_status(FileVerificationStatus status, string reason, int expected)   // C7
        {
            SessionReturns(Session(completion: true));
            _driver.Setup(d => d.CompleteUploadAsync(It.IsAny<CompleteUploadRequest>())).ReturnsAsync(new CompleteUploadResponse
            {
                IsSuccess = true, FileId = "file-1", FileVersionId = "v-1", VerificationStatus = status, RejectionReason = reason,
            });
            using var file = await PdfAsync();

            var act = () => _sut.StoreAsync(file, Anonymous);

            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.StatusCode.Should().Be(expected);
        }

        [Fact]
        public async Task Verified_completion_returns_the_file_id()
        {
            SessionReturns(Session(completion: true));
            _driver.Setup(d => d.CompleteUploadAsync(It.Is<CompleteUploadRequest>(r => r.FileId == "file-1" && r.FileVersionId == "v-1")))
                .ReturnsAsync(new CompleteUploadResponse { IsSuccess = true, VerificationStatus = FileVerificationStatus.Verified });
            using var file = await PdfAsync();

            (await _sut.StoreAsync(file, Anonymous)).Should().Be("file-1");
        }

        [Fact]
        public async Task Provider_failure_or_driver_exception_is_503()   // C8
        {
            SessionReturns(Session());
            _provider.Status = HttpStatusCode.InternalServerError;
            using var file = await PdfAsync();

            var act = () => _sut.StoreAsync(file, Anonymous);
            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.StatusCode.Should().Be(503);

            _driver.Setup(d => d.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                .ThrowsAsync(new InvalidOperationException("db down"));
            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.Message.Should().Be("File storage is unavailable");
        }

        [Fact]
        public async Task Delete_is_permanent_and_reports_success()
        {
            _driver.Setup(d => d.DeleteFileAsync(It.Is<DeleteFileRequest>(r => r.FileId == "file-1" && r.Permanent)))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            (await _sut.DeleteAsync("file-1", Anonymous)).Should().BeTrue();
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
            public HttpMethod? Method { get; private set; }
            public byte[] Body { get; private set; } = [];
            public List<string> Headers { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Method = request.Method;
                Body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
                Headers.AddRange(request.Headers.Select(h => h.Key));
                if (request.Content is not null) Headers.AddRange(request.Content.Headers.Select(h => h.Key));
                return new HttpResponseMessage(Status);
            }
        }
    }
}
