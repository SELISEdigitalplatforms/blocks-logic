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
                Options.Create(new WebhookUploadOptions()),
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

        private static WebhookFileTarget Caller => new() { TenantId = "tenant-1", Caller = TestBlocksContext.Create("tenant-1", "user-7") };

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

            var fileId = await _sut.StoreAsync(file, Caller);

            fileId.Should().Be("file-1");
            sent!.Name.Should().Be("upload.pdf");
            // Storage's default folder and configuration, always (SPEC10 N2).
            sent.ParentDirectoryId.Should().BeNull();
            sent.ConfigurationName.Should().BeNull();
            sent.AccessModifier.Should().Be("Private");
            sent.SizeInBytes.Should().Be(3);
            sent.ContentType.Should().Be("application/pdf");
            _provider.Method.Should().Be(HttpMethod.Put);
            _provider.Body.Should().Equal(1, 2, 3);
            _provider.Headers.Should().Contain("x-ms-blob-type");
        }

        [Fact]
        public async Task Without_a_caller_nothing_reaches_storage()   // SPEC10 K6
        {
            using var file = await PdfAsync();

            var act = () => _sut.StoreAsync(file, new WebhookFileTarget { TenantId = "tenant-1" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            _driver.VerifyNoOtherCalls();
            (await _sut.DeleteAsync("file-1", new WebhookFileTarget { TenantId = "tenant-1" })).Should().BeFalse();
            _driver.VerifyNoOtherCalls();
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

        [Fact]
        public async Task A_missing_default_folder_is_503()   // SPEC10 K5
        {
            SessionReturns(new GetPreSignedUrlForUploadResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { ["ParentDirectoryId"] = "default_directory_not_found" },
            });
            using var file = await PdfAsync();

            var thrown = (await ((Func<Task>)(() => _sut.StoreAsync(file, Caller))).Should().ThrowAsync<WebhookIntakeException>()).Which;

            (thrown.StatusCode, thrown.Message).Should().Be((503, "File storage is not set up for this project"));
        }

        [Theory]
        [InlineData("declared_size_exceeds_maximum_allowed", 413, "File 'upload.pdf' exceeds the storage limit")]
        [InlineData("forbidden", 403, "Not allowed to store files in the configured folder")]
        [InlineData("something_else", 422, "File 'upload.pdf' was rejected by storage")]
        public async Task Refused_upload_session_maps_to_a_status(string error, int status, string message)   // C6, A6
        {
            var key = error == "forbidden" ? "access" : "SizeInBytes";
            SessionReturns(new GetPreSignedUrlForUploadResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { [key] = error },
            });
            using var file = await PdfAsync();

            var act = () => _sut.StoreAsync(file, Caller);

            var thrown = (await act.Should().ThrowAsync<WebhookIntakeException>()).Which;
            (thrown.StatusCode, thrown.Message).Should().Be((status, message));
            _provider.Method.Should().BeNull("nothing is uploaded after a refusal");
        }

        [Theory]
        [InlineData("File extension .exe is not supported", "File 'upload.pdf' was rejected by storage: File extension .exe is not supported")]
        [InlineData("https://storage.example/upload?sig=secret", "File 'upload.pdf' was rejected by storage")]
        [InlineData(null, "File 'upload.pdf' was rejected by storage")]
        public async Task Refusal_without_errors_carries_storages_reason_but_never_a_url(string? reason, string message)   // C13
        {
            // The upload-session endpoint reports name/extension refusals in UploadUrl, with no Errors.
            SessionReturns(new GetPreSignedUrlForUploadResponse { IsSuccess = false, UploadUrl = reason });
            using var file = await PdfAsync();

            var thrown = (await ((Func<Task>)(() => _sut.StoreAsync(file, Caller))).Should().ThrowAsync<WebhookIntakeException>()).Which;

            (thrown.StatusCode, thrown.Message).Should().Be((422, message));
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

            var act = () => _sut.StoreAsync(file, Caller);

            (await act.Should().ThrowAsync<WebhookIntakeException>()).Which.StatusCode.Should().Be(expected);
        }

        [Fact]
        public async Task Verified_completion_returns_the_file_id()
        {
            SessionReturns(Session(completion: true));
            _driver.Setup(d => d.CompleteUploadAsync(It.Is<CompleteUploadRequest>(r => r.FileId == "file-1" && r.FileVersionId == "v-1")))
                .ReturnsAsync(new CompleteUploadResponse { IsSuccess = true, VerificationStatus = FileVerificationStatus.Verified });
            using var file = await PdfAsync();

            (await _sut.StoreAsync(file, Caller)).Should().Be("file-1");
        }

        [Fact]
        public async Task Provider_failure_or_driver_exception_is_503()   // C8
        {
            SessionReturns(Session());
            _provider.Status = HttpStatusCode.InternalServerError;
            using var file = await PdfAsync();

            var act = () => _sut.StoreAsync(file, Caller);
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

            (await _sut.DeleteAsync("file-1", Caller)).Should().BeTrue();
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
