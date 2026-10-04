using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;

namespace XUnitTest.Workflow
{
    public class ExecutionLogServiceTests
    {
        private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
        private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        private sealed class FixedTime : TimeProvider
        {
            public DateTimeOffset Value { get; set; } = new(Now);
            public override DateTimeOffset GetUtcNow() => Value;
        }

        private sealed class FixedRetention(int days) : IExecutionLogRetentionProvider
        {
            public Task<int> GetRetentionDaysAsync(CancellationToken ct = default) => Task.FromResult(days);
        }

        private readonly Mock<IWorkflowExecutionRepository> _repository = new();
        private readonly Mock<IExecutionLogStore> _store = new();
        private readonly ExecutionLogOptions _options = new();

        private ExecutionLogService Service(int retentionDays = 30, IExecutionLogStore? store = null) => new(
            _repository.Object,
            store ?? _store.Object,
            new FixedRetention(retentionDays),
            Options.Create(_options),
            NullLogger<ExecutionLogService>.Instance,
            new FixedTime());

        private WorkflowExecutionEntity Execution(
            string? traceId = TraceId,
            DateTime? startedAt = null,
            WorkflowExecutionStatus status = WorkflowExecutionStatus.Completed,
            DateTime? finishedAt = null)
        {
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = "tenant-1",
                WorkflowId = "wf-1",
                WorkflowName = "Orders",
                WorkflowSnapshot = new WorkflowEntity
                {
                    ItemId = "wf-1",
                    Name = "Orders",
                    TenantId = "tenant-1",
                    Nodes =
                    [
                        new NodeEntity { Id = "n1", Name = "Incoming order", Type = "webhook", Category = "trigger", Version = "v1", Position = new Position() },
                        new NodeEntity { Id = "n2", Name = "Call CRM", Type = "httpRequest", Category = "action", Version = "v1", Position = new Position() },
                    ],
                },
                TriggerMetadata = new TriggerMetadata(),
                TraceId = traceId,
                StartedAt = startedAt ?? Now.AddMinutes(-10),
                Status = status,
                FinishedAt = finishedAt ?? (status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed ? Now.AddMinutes(-9) : null),
            };
            _repository.Setup(r => r.GetByIdAsync("exec-1", "tenant-1")).ReturnsAsync(execution);
            return execution;
        }

        private void StoreReturns(params RawExecutionLogLine[] lines)
            => _store.Setup(s => s.QueryAsync(It.IsAny<ExecutionLogQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(lines);

        private static RawExecutionLogLine Line(int second, string message, string service = "blocks-logic-worker", string level = "Information")
            => new(Now.AddMinutes(-10).AddSeconds(second), level, message, service);

        private static WorkflowExecutionLogsGetRequestDto Request => new() { ExecutionId = "exec-1" };

        [Fact]
        public async Task OtherTenantsExecution_IsNotFound_AndTheStoreIsNeverCalled()
        {
            Execution();

            var act = () => Service().GetAsync("tenant-2", Request);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
            _store.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task NullTraceId_IsNotRecorded_WithRetentionSet_AndNoQuery()
        {
            Execution(traceId: null);

            var result = await Service(retentionDays: 7).GetAsync("tenant-1", Request);

            result.IsSuccess.Should().BeTrue();
            result.Data!.Availability.Should().Be(ExecutionLogsAvailability.NotRecorded);
            result.Data.RetentionDays.Should().Be(7);
            result.Data.ExpiresAt.Should().BeNull();
            _store.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(30)]
        [InlineData(7)]
        public async Task Expiry_Boundary(int days)
        {
            Execution(startedAt: Now.AddDays(-days));
            StoreReturns();

            var exactlyN = await Service(days).GetAsync("tenant-1", Request);
            exactlyN.Data!.Availability.Should().Be(ExecutionLogsAvailability.Available);

            Execution(startedAt: Now.AddDays(-days).AddSeconds(-1));
            _store.Invocations.Clear();

            var pastN = await Service(days).GetAsync("tenant-1", Request);
            pastN.Data!.Availability.Should().Be(ExecutionLogsAvailability.Expired);
            pastN.Data.RetentionDays.Should().Be(days);
            pastN.Data.ExpiresAt.Should().Be(Now.AddSeconds(-1));
            _store.Invocations.Should().BeEmpty();
        }

        [Fact]
        public async Task Available_ParsesSortsResolvesNodes_AndDropsForeignLines()
        {
            Execution();
            StoreReturns(
                Line(2, "[wf:node.started] [node:n2#2] Node 'Call CRM' (httpRequest v1) started."),
                Line(1, "[wf:node.completed] [node:n1#1] Node completed in 38 ms.", service: "blocks-logic"),
                Line(1, "[wf:node.started] [node:n1#1] Node 'Incoming order' (webhook v1) started."),
                Line(0, "[wf:execution.created] Execution created. Mode Production, trigger webhook.", service: "blocks-logic"),
                Line(3, "Response body: {secret}"),
                Line(4, "[wf:node.failed] [node:n2#2] Node failed after 412 ms (HttpRequestException).", level: "Error"));

            var result = await Service().GetAsync("tenant-1", Request);

            var data = result.Data!;
            data.Availability.Should().Be(ExecutionLogsAvailability.Available);
            data.TraceId.Should().Be(TraceId);
            data.IsTruncated.Should().BeFalse();
            data.Logs.Select(l => l.Stage).Should().Equal(
                "execution.created", "node.completed", "node.started", "node.started", "node.failed");
            data.Logs[0].NodeId.Should().BeNull();
            data.Logs[0].Source.Should().Be("api");
            data.Logs[1].Source.Should().Be("api");     // api wins the tie at second 1
            data.Logs[2].Source.Should().Be("worker");
            data.Logs[3].NodeName.Should().Be("Call CRM");
            data.Logs[3].NodeType.Should().Be("httpRequest");
            data.Logs[3].RunIndex.Should().Be(2);
            data.Logs[3].Message.Should().Be("Node 'Call CRM' (httpRequest v1) started.");
            data.Logs[4].Level.Should().Be("Error");
            data.Logs.Should().OnlyContain(l => l.Timestamp.Kind == DateTimeKind.Utc);
        }

        [Fact]
        public async Task QueryWindow_SpansStartMinusOneMinute_ToFinishPlusGrace()
        {
            var execution = Execution();
            ExecutionLogQuery? captured = null;
            _store.Setup(s => s.QueryAsync(It.IsAny<ExecutionLogQuery>(), It.IsAny<CancellationToken>()))
                .Callback<ExecutionLogQuery, CancellationToken>((q, _) => captured = q)
                .ReturnsAsync(Array.Empty<RawExecutionLogLine>());

            await Service().GetAsync("tenant-1", Request);

            captured.Should().Be(new ExecutionLogQuery(
                "tenant-1", TraceId, execution.StartedAt.AddMinutes(-1), execution.FinishedAt!.Value.AddSeconds(120), 2000));
        }

        [Fact]
        public async Task MoreThanMaxLines_IsTruncated()
        {
            _options.MaxLines = 3;
            Execution();
            StoreReturns(Enumerable.Range(0, 5).Select(i => Line(i, $"[wf:node.input] [node:n1#1] line {i}")).ToArray());

            var result = await Service().GetAsync("tenant-1", Request);

            result.Data!.IsTruncated.Should().BeTrue();
            result.Data.Logs.Select(l => l.Message).Should().Equal("line 0", "line 1", "line 2");
        }

        [Fact]
        public async Task StoreThrows_IsSourceUnavailable_NotAnError()
        {
            Execution();
            _store.Setup(s => s.QueryAsync(It.IsAny<ExecutionLogQuery>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException());

            var result = await Service().GetAsync("tenant-1", Request);

            result.IsSuccess.Should().BeTrue();
            result.Data!.Availability.Should().Be(ExecutionLogsAvailability.SourceUnavailable);
            result.Data.RetentionDays.Should().Be(30);
        }

        [Fact]
        public async Task EmptyLogConnectionString_IsSourceUnavailable()
        {
            Execution();
            var secret = new Mock<Blocks.Genesis.IBlocksSecret>();
            secret.SetupGet(s => s.LogConnectionString).Returns(string.Empty);
            secret.SetupGet(s => s.LogDatabaseName).Returns("Logs");
            var store = new MongoExecutionLogStore(Mock.Of<Blocks.Genesis.IDbContextProvider>(), secret.Object, Options.Create(_options));

            var result = await Service(store: store).GetAsync("tenant-1", Request);

            result.Data!.Availability.Should().Be(ExecutionLogsAvailability.SourceUnavailable);
        }

        [Theory]
        [InlineData(WorkflowExecutionStatus.Running, null, true)]         // running
        [InlineData(WorkflowExecutionStatus.Completed, 30, true)]         // finished 30 s ago
        [InlineData(WorkflowExecutionStatus.Failed, 30, true)]
        [InlineData(WorkflowExecutionStatus.Completed, 600, false)]       // finished 10 min ago
        public async Task MayStillArrive(WorkflowExecutionStatus status, int? finishedSecondsAgo, bool expected)
        {
            Execution(status: status, finishedAt: finishedSecondsAgo is null ? null : Now.AddSeconds(-finishedSecondsAgo.Value));
            StoreReturns();

            var result = await Service().GetAsync("tenant-1", Request);

            result.Data!.MayStillArrive.Should().Be(expected);
        }
    }
}
