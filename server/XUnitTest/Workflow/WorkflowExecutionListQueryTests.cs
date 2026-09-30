using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    public class WorkflowExecutionListQueryTests
    {
        private readonly Mock<IWorkflowExecutionRepository> _executions = new();
        private readonly WorkflowExecutionService _sut;

        public WorkflowExecutionListQueryTests()
        {
            _sut = new WorkflowExecutionService(
                Mock.Of<IWorkflowRepository>(),
                _executions.Object,
                Mock.Of<IMessageClient>(),
                Mock.Of<ILogger<WorkflowExecutionService>>(),
                Mock.Of<IWorkflowEngineService>(),
                Mock.Of<IWorkflowVersionRepository>(),
                Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IWorkflowAuthService>(),
                Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IDelegationGrantFactory>());
        }

        [Fact]
        public void OlderCursor_SameTimestamp_SmallerIdIsOlder_GreaterStartedAtIsNot()
        {
            var cursorTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            WorkflowExecutionListCursor.IsStrictlyOlder(cursorTime, "a", cursorTime, "b").Should().BeTrue();
            WorkflowExecutionListCursor.IsStrictlyOlder(cursorTime, "b", cursorTime, "a").Should().BeFalse();
            WorkflowExecutionListCursor.IsStrictlyOlder(cursorTime, "a", cursorTime, "a").Should().BeFalse();
            WorkflowExecutionListCursor.IsStrictlyOlder(cursorTime.AddSeconds(1), "a", cursorTime, "z").Should().BeFalse();
            WorkflowExecutionListCursor.IsStrictlyOlder(cursorTime.AddSeconds(-1), "z", cursorTime, "a").Should().BeTrue();
        }

        [Fact]
        public void NewerCursor_GreaterStartedAtOrGreaterIdAtTheSameTime()
        {
            var cursorTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            WorkflowExecutionListCursor.IsStrictlyNewer(cursorTime.AddSeconds(1), "a", cursorTime, "b").Should().BeTrue();
            WorkflowExecutionListCursor.IsStrictlyNewer(cursorTime, "b", cursorTime, "a").Should().BeTrue();
            WorkflowExecutionListCursor.IsStrictlyNewer(cursorTime, "a", cursorTime, "b").Should().BeFalse();
            WorkflowExecutionListCursor.IsStrictlyNewer(cursorTime, "a", cursorTime, "a").Should().BeFalse();
            WorkflowExecutionListCursor.IsStrictlyNewer(cursorTime.AddSeconds(-1), "z", cursorTime, "a").Should().BeFalse();
        }

        [Fact]
        public async Task GetExecutions_WithoutPageSize_ReturnsTheFullList()
        {
            _executions
                .Setup(r => r.GetByWorkflowIdAsync("wf", "tenant-1"))
                .ReturnsAsync(
                [
                    Execution("e2", "wf"),
                    Execution("e1", "wf"),
                ]);

            var result = await _sut.GetExecutionsByWorkflowIdAsync("tenant-1", new WorkflowExecutionsGetRequestDto
            {
                WorkflowId = "wf",
                RefreshIds = ["ignored"],
            });

            result.HttpStatus.Should().Be(200);
            result.TotalCount.Should().Be(2);
            result.Data.Should().HaveCount(2);
            result.Data!.Select(item => item.Id).Should().Equal("e2", "e1");
            result.Refreshed.Should().BeNull();
            _executions.Verify(r => r.GetPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetExecutions_FirstPage_UsesTheUnpagedCount()
        {
            _executions
                .Setup(r => r.GetPageAsync("wf", "tenant-1", 20))
                .ReturnsAsync([Row("newest")]);
            _executions
                .Setup(r => r.CountByWorkflowIdAsync("wf", "tenant-1"))
                .ReturnsAsync(40);

            var result = await _sut.GetExecutionsByWorkflowIdAsync("tenant-1", new WorkflowExecutionsGetRequestDto
            {
                WorkflowId = "wf",
                PageSize = 20,
            });

            result.HttpStatus.Should().Be(200);
            result.TotalCount.Should().Be(40);
            result.Data.Should().ContainSingle().Which.Id.Should().Be("newest");
            _executions.Verify(r => r.GetByWorkflowIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetExecutions_UnknownBeforeId_ReturnsEmptyPage()
        {
            _executions
                .Setup(r => r.GetByIdAsync("missing", "tenant-1"))
                .ReturnsAsync((WorkflowExecutionEntity?)null);

            var result = await _sut.GetExecutionsByWorkflowIdAsync("tenant-1", new WorkflowExecutionsGetRequestDto
            {
                WorkflowId = "wf",
                PageSize = 20,
                BeforeId = "missing",
            });

            result.HttpStatus.Should().Be(200);
            result.Data.Should().BeEmpty();
            result.TotalCount.Should().Be(0);
            _executions.Verify(
                r => r.GetOlderThanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<int>()),
                Times.Never);
        }

        [Fact]
        public async Task GetExecutions_BeforeIdFromAnotherWorkflow_ReturnsEmptyPage()
        {
            _executions
                .Setup(r => r.GetByIdAsync("other-row", "tenant-1"))
                .ReturnsAsync(Execution("other-row", "other-workflow"));

            var result = await _sut.GetExecutionsByWorkflowIdAsync("tenant-1", new WorkflowExecutionsGetRequestDto
            {
                WorkflowId = "wf",
                PageSize = 20,
                BeforeId = "other-row",
            });

            result.HttpStatus.Should().Be(200);
            result.Data.Should().BeEmpty();
            _executions.Verify(
                r => r.GetOlderThanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<int>()),
                Times.Never);
        }

        [Fact]
        public async Task GetExecutions_AfterId_PutsRefreshOnRefreshedAndDropsOtherWorkflows()
        {
            var started = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            _executions
                .Setup(r => r.GetByIdAsync("head", "tenant-1"))
                .ReturnsAsync(Execution("head", "wf", started));
            _executions
                .Setup(r => r.GetNewerThanAsync("wf", "tenant-1", started, "head", 20))
                .ReturnsAsync([Row("newer", status: WorkflowExecutionStatus.Running)]);
            _executions
                .Setup(r => r.CountByWorkflowIdAsync("wf", "tenant-1"))
                .ReturnsAsync(8);
            _executions
                .Setup(r => r.GetListItemsByIdsAsync("wf", "tenant-1", It.IsAny<IReadOnlyCollection<string>>()))
                .ReturnsAsync(
                [
                    Row("keep", status: WorkflowExecutionStatus.Completed),
                    Row("drop", workflowId: "other", status: WorkflowExecutionStatus.Failed),
                ]);

            var result = await _sut.GetExecutionsByWorkflowIdAsync("tenant-1", new WorkflowExecutionsGetRequestDto
            {
                WorkflowId = "wf",
                PageSize = 20,
                AfterId = "head",
                RefreshIds = ["keep", "drop", "  keep  "],
            });

            result.HttpStatus.Should().Be(200);
            result.TotalCount.Should().Be(8);
            result.Data.Should().ContainSingle().Which.Id.Should().Be("newer");
            result.Refreshed.Should().ContainSingle().Which.Id.Should().Be("keep");
            result.Refreshed![0].Status.Should().Be(WorkflowExecutionStatus.Completed);
        }

        [Fact]
        public async Task GetExecutions_RefreshIds_AreCappedAt50()
        {
            IReadOnlyCollection<string>? seen = null;
            var started = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            _executions
                .Setup(r => r.GetByIdAsync("head", "tenant-1"))
                .ReturnsAsync(Execution("head", "wf", started));
            _executions
                .Setup(r => r.GetNewerThanAsync("wf", "tenant-1", started, "head", 20))
                .ReturnsAsync([]);
            _executions
                .Setup(r => r.CountByWorkflowIdAsync("wf", "tenant-1"))
                .ReturnsAsync(1);
            _executions
                .Setup(r => r.GetListItemsByIdsAsync("wf", "tenant-1", It.IsAny<IReadOnlyCollection<string>>()))
                .Callback<string, string, IReadOnlyCollection<string>>((_, _, ids) => seen = ids)
                .ReturnsAsync([]);

            var ids = Enumerable.Range(0, 51).Select(i => $"id-{i}").ToList();
            await _sut.GetExecutionsByWorkflowIdAsync("tenant-1", new WorkflowExecutionsGetRequestDto
            {
                WorkflowId = "wf",
                PageSize = 20,
                AfterId = "head",
                RefreshIds = ids,
            });

            seen.Should().NotBeNull();
            seen.Should().HaveCount(50);
            seen.Should().NotContain("id-50");
        }

        [Fact]
        public async Task GetExecutions_InvalidPaging_Returns400AndEmptyData()
        {
            WorkflowExecutionsGetRequestDto[] cases =
            [
                new() { WorkflowId = "wf", PageSize = 0 },
                new() { WorkflowId = "wf", PageSize = 101 },
                new() { WorkflowId = "wf", PageSize = 20, BeforeId = "a", AfterId = "b" },
                new() { WorkflowId = "wf", BeforeId = "a" },
                new() { WorkflowId = "wf", AfterId = "b" },
            ];

            foreach (var dto in cases)
            {
                var result = await _sut.GetExecutionsByWorkflowIdAsync("tenant-1", dto);
                result.HttpStatus.Should().Be(400, because: $"pageSize={dto.PageSize} before={dto.BeforeId} after={dto.AfterId}");
                result.Data.Should().BeEmpty();
                result.TotalCount.Should().Be(0);
                result.Errors.Should().NotBeNull();
            }

            _executions.Verify(r => r.GetByWorkflowIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _executions.Verify(r => r.GetPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        private static WorkflowExecutionEntity Execution(string id, string workflowId, DateTime? startedAt = null) => new()
        {
            Id = id,
            TenantId = "tenant-1",
            WorkflowId = workflowId,
            WorkflowName = "name",
            WorkflowSnapshot = new WorkflowEntity { TenantId = "tenant-1" },
            TriggerMetadata = new TriggerMetadata(),
            StartedAt = startedAt ?? DateTime.UtcNow,
        };

        private static WorkflowExecutionListRow Row(
            string id,
            string workflowId = "wf",
            WorkflowExecutionStatus status = WorkflowExecutionStatus.Running) => new()
        {
            Id = id,
            WorkflowId = workflowId,
            WorkflowName = "name",
            Status = status,
            ExecutionMode = WorkflowExecutionMode.Test,
            StartedAt = DateTime.UtcNow,
        };
    }
}
