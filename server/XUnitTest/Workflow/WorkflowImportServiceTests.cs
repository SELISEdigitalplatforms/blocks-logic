using System.Net;
using System.Text;
using Blocks.Genesis;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StorageDriver;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Events;
using Workflow.DomainService.Import;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    public class WorkflowImportServiceTests : IDisposable
    {
        private readonly Mock<IMessageClient> _messages = new();
        private readonly Mock<IStorageDriverService> _storage = new();
        private readonly Mock<IWorkflowRepository> _repository = new();
        private readonly Mock<IWorkflowNotificationService> _notifications = new();
        private readonly Mock<IWorkflowImportTenantSlugResolver> _slug = new();
        private readonly Mock<IHttpClientFactory> _httpFactory = new();
        private readonly WorkflowImportService _sut;
        private StubHandler? _handler;

        public WorkflowImportServiceTests()
        {
            TestBlocksContext.Set("tenant-abc", "user-abc");
            _slug.Setup(s => s.ResolveAsync(It.IsAny<string>())).ReturnsAsync("dslug");
            _notifications.Setup(n => n.NotifyImportAsync(
                    It.IsAny<List<string>>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>()))
                .ReturnsAsync(true);
            _sut = new WorkflowImportService(
                _messages.Object,
                _storage.Object,
                _httpFactory.Object,
                _repository.Object,
                _notifications.Object,
                _slug.Object,
                NullLogger<WorkflowImportService>.Instance);
        }

        public void Dispose() => TestBlocksContext.Clear();

        private void SetupDownload(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _storage.Setup(s => s.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .ReturnsAsync(new FileResponse { Url = "https://blob.example/file", Name = "wf.json" });
            _handler = new StubHandler(status, json);
            _httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(_handler, disposeHandler: false));
        }

        [Fact]
        public async Task EnqueueAsync_PublishesToImportQueue()
        {
            ConsumerMessage<WorkflowImportEvent>? captured = null;
            _messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<WorkflowImportEvent>>()))
                .Callback<ConsumerMessage<WorkflowImportEvent>>(msg => captured = msg)
                .Returns(Task.CompletedTask);

            var result = await _sut.EnqueueAsync(new WorkflowImportRequestDto
            {
                FileId = "file-1",
                MessageCoRelationId = "cor-1",
            });

            result.IsSuccess.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.ConsumerName.Should().Be(LogicConstants.WorkflowImportQueue);
            captured.Payload.FileId.Should().Be("file-1");
            captured.Payload.TenantId.Should().Be("tenant-abc");
            captured.Payload.UserId.Should().Be("user-abc");
        }

        [Fact]
        public async Task ImportAsync_CreatesUnpublishedWorkflowAndNotifiesSuccess()
        {
            var json = """{"name":"Imported","settings":{},"nodes":[],"edges":[]}""";
            SetupDownload(json);
            WorkflowEntity? saved = null;
            _repository.Setup(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()))
                .Callback<WorkflowEntity>(w => saved = w)
                .Returns(Task.CompletedTask);

            await _sut.ImportAsync(new WorkflowImportEvent
            {
                FileId = "file-1",
                MessageCoRelationId = "cor-1",
                TenantId = "tenant-abc",
                UserId = "user-abc",
            });

            saved.Should().NotBeNull();
            saved!.Name.Should().Be("Imported");
            saved.IsPublished.Should().BeFalse();
            saved.IsDirty.Should().BeTrue();
            saved.TenantId.Should().Be("tenant-abc");
            _notifications.Verify(n => n.NotifyImportAsync(
                It.Is<List<string>>(ids => ids.Contains("user-abc")),
                "cor-1",
                true,
                "Workflow imported",
                It.IsAny<string>(),
                saved.ItemId,
                0), Times.Once);
        }

        [Fact]
        public async Task ImportAsync_NotifiesFailureWhenFileMissing()
        {
            _storage.Setup(s => s.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .ReturnsAsync((FileResponse?)null);

            await _sut.ImportAsync(new WorkflowImportEvent
            {
                FileId = "missing",
                MessageCoRelationId = "cor-fail",
                TenantId = "tenant-abc",
                UserId = "user-abc",
            });

            _repository.Verify(r => r.CreateWorkflowAsync(It.IsAny<WorkflowEntity>()), Times.Never);
            _notifications.Verify(n => n.NotifyImportAsync(
                It.IsAny<List<string>>(),
                "cor-fail",
                false,
                "Workflow import failed",
                It.IsAny<string>(),
                null,
                0), Times.Once);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
