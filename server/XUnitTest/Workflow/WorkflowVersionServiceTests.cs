using Blocks.Genesis;
using DomainService.Workflow.Dtos;
using DomainService.Workflow.Entities;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Workflow
{
    public class WorkflowVersionServiceTests : IDisposable
    {
        private readonly Mock<IWorkflowVersionRepository> _versionRepo = new();
        private readonly Mock<IWorkflowRepository> _workflowRepo = new();
        private readonly WorkflowVersionService _service;

        public WorkflowVersionServiceTests()
        {
            TestBlocksContext.Set("tenant-wf", "user-wf");
            _service = new WorkflowVersionService(_versionRepo.Object, _workflowRepo.Object,
                Mock.Of<ILogger<WorkflowVersionService>>());
        }

        public void Dispose() => TestBlocksContext.Clear();

        private static WorkflowEntity Workflow(string tenant = "tenant-wf") =>
            new() { ItemId = "wf1", TenantId = tenant, Name = "wf" };

        private static WorkflowVersionEntity Version() =>
            new() { ItemId = "v1", WorkflowId = "wf1", TenantId = "tenant-wf", Name = "v", Snapshot = Workflow() };

        // ---------- CreateVersion ----------
        [Fact]
        public async Task CreateVersion_WorkflowNotFound_ReturnsError()
        {
            _workflowRepo.Setup(r => r.GetWorkflowAsync("tenant-wf", "wf1")).ReturnsAsync((WorkflowEntity)null!);

            var result = await _service.CreateVersionAsync("tenant-wf", new WorkflowVersionCreateRequestDto { WorkflowId = "wf1", Name = "v" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task CreateVersion_Success_ReturnsItemId()
        {
            _workflowRepo.Setup(r => r.GetWorkflowAsync("tenant-wf", "wf1")).ReturnsAsync(Workflow());

            var result = await _service.CreateVersionAsync("tenant-wf", new WorkflowVersionCreateRequestDto { WorkflowId = "wf1", Name = "v" });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrEmpty();
            _versionRepo.Verify(r => r.CreateWorkflowVersionAsync(It.IsAny<WorkflowVersionEntity>()), Times.Once);
        }

        [Fact]
        public async Task CreateVersion_RepositoryThrows_ReturnsError()
        {
            _workflowRepo.Setup(r => r.GetWorkflowAsync("tenant-wf", "wf1")).ReturnsAsync(Workflow());
            _versionRepo.Setup(r => r.CreateWorkflowVersionAsync(It.IsAny<WorkflowVersionEntity>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var result = await _service.CreateVersionAsync("tenant-wf", new WorkflowVersionCreateRequestDto { WorkflowId = "wf1", Name = "v" });

            result.IsSuccess.Should().BeFalse();
        }

        // ---------- UpdateVersion ----------
        [Fact]
        public async Task UpdateVersion_NotFound_ReturnsError()
        {
            _versionRepo.Setup(r => r.GetWorkflowVersionAsync("tenant-wf", "v1")).ReturnsAsync((WorkflowVersionEntity)null!);

            var result = await _service.UpdateVersionAsync("tenant-wf", new WorkflowVersionUpdateRequestDto { VersionId = "v1", Name = "n" });

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateVersion_Success_ReturnsItemId()
        {
            _versionRepo.Setup(r => r.GetWorkflowVersionAsync("tenant-wf", "v1")).ReturnsAsync(Version());

            var result = await _service.UpdateVersionAsync("tenant-wf", new WorkflowVersionUpdateRequestDto { VersionId = "v1", Name = "renamed" });

            result.IsSuccess.Should().BeTrue();
            _versionRepo.Verify(r => r.UpdateWorkflowVersionAsync("tenant-wf", "v1", It.IsAny<WorkflowVersionEntity>()), Times.Once);
        }

        [Fact]
        public async Task UpdateVersion_RepositoryThrows_ReturnsError()
        {
            _versionRepo.Setup(r => r.GetWorkflowVersionAsync("tenant-wf", "v1")).ReturnsAsync(Version());
            _versionRepo.Setup(r => r.UpdateWorkflowVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkflowVersionEntity>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var result = await _service.UpdateVersionAsync("tenant-wf", new WorkflowVersionUpdateRequestDto { VersionId = "v1", Name = "n" });

            result.IsSuccess.Should().BeFalse();
        }

        // ---------- GetWorkflowVersions ----------
        [Fact]
        public async Task GetWorkflowVersions_WorkflowNotFound_ReturnsError()
        {
            _workflowRepo.Setup(r => r.GetWorkflowAsync("tenant-wf", "wf1")).ReturnsAsync((WorkflowEntity)null!);

            var result = await _service.GetWorkflowVersionsAsync("tenant-wf", new WorkflowGetVersionsRequestDto { WorkflowId = "wf1" });

            result.Data.Should().BeNull();
            result.TotalCount.Should().Be(0);
        }

        [Fact]
        public async Task GetWorkflowVersions_Success_ReturnsSummariesWithPublishedFlag()
        {
            var workflow = Workflow();
            workflow.PublishedVersionId = "v1";
            _workflowRepo.Setup(r => r.GetWorkflowAsync("tenant-wf", "wf1")).ReturnsAsync(workflow);
            _versionRepo.Setup(r => r.GetWorkflowVersionsAsync("tenant-wf", "wf1"))
                .ReturnsAsync(new List<WorkflowVersionEntity> { Version() });

            var result = await _service.GetWorkflowVersionsAsync("tenant-wf", new WorkflowGetVersionsRequestDto { WorkflowId = "wf1" });

            result.TotalCount.Should().Be(1);
            result.Data.Should().ContainSingle(s => s.IsPublished);
        }

        [Fact]
        public async Task GetWorkflowVersions_RepositoryThrows_ReturnsError()
        {
            _workflowRepo.Setup(r => r.GetWorkflowAsync("tenant-wf", "wf1")).ReturnsAsync(Workflow());
            _versionRepo.Setup(r => r.GetWorkflowVersionsAsync("tenant-wf", "wf1"))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var result = await _service.GetWorkflowVersionsAsync("tenant-wf", new WorkflowGetVersionsRequestDto { WorkflowId = "wf1" });

            result.Data.Should().BeNull();
        }
    }
}
