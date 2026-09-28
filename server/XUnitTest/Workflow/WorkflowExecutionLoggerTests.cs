using System.Diagnostics;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;

namespace XUnitTest.Workflow
{
    public class WorkflowExecutionLoggerTests
    {
        private const string ExecutionTraceId = "0af7651916cd43dd8448eb211c80319c";

        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new();
            public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        }

        private static (SerilogLoggerFactory factory, CollectingSink sink) BuildGenesisLikeFactory()
        {
            var sink = new CollectingSink();
            // Same enrichment order as Genesis: FromLogContext, then TraceContextEnricher.
            var serilog = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.With<TraceContextEnricher>()
                .WriteTo.Sink(sink)
                .CreateLogger();
            return (new SerilogLoggerFactory(serilog, dispose: true), sink);
        }

        private static WorkflowExecutionEntity Execution(string? traceId = ExecutionTraceId) => new()
        {
            Id = "exec-1",
            TenantId = "tenant-1",
            WorkflowId = "wf-1",
            WorkflowName = "Orders",
            WorkflowSnapshot = new WorkflowEntity { ItemId = "wf-1", Name = "Orders", TenantId = "tenant-1" },
            TriggerMetadata = new TriggerMetadata(),
            TraceId = traceId,
        };

        private static string? ScalarProperty(LogEvent e, string name)
            => e.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar ? scalar.Value?.ToString() : null;

        [Fact]
        public void StageLine_IsStampedWithExecutionTraceId_NotTheAmbientActivity_AndDoesNotLeak()
        {
            var (factory, sink) = BuildGenesisLikeFactory();
            using var _ = factory;
            var stageLogger = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>());
            var plainLogger = factory.CreateLogger("Plain");

            using var activity = new Activity("aspnet-request").SetIdFormat(ActivityIdFormat.W3C).Start();
            var ambientTraceId = activity.TraceId.ToHexString();
            ambientTraceId.Should().NotBe(ExecutionTraceId);

            stageLogger.For(Execution()).Info(ExecutionLogStages.ExecutionCreated, "Execution created.");
            plainLogger.LogInformation("An ordinary line.");

            sink.Events.Should().HaveCount(2);
            ScalarProperty(sink.Events[0], "TraceId").Should().Be(ExecutionTraceId);
            ScalarProperty(sink.Events[0], "TenantId").Should().Be("tenant-1");
            ScalarProperty(sink.Events[1], "TraceId").Should().Be(ambientTraceId);
        }

        [Fact]
        public void LiteralFormat_RendersWithoutQuotes_AndBracesInValuesStayLiteral()
        {
            var (factory, sink) = BuildGenesisLikeFactory();
            using var _ = factory;
            var log = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(Execution());

            log.ForNode("n1", 2).Info(ExecutionLogStages.NodeStarted, "Node '{NodeName:l}' started.", "Call {x} CRM");

            sink.Events.Single().RenderMessage().Should().Be("[wf:node.started] [node:n1#2] Node 'Call {x} CRM' started.");
        }

        [Theory]
        [InlineData(null, null, "[wf:execution.created] Execution created.")]
        [InlineData("node-a", 3, "[wf:execution.created] [node:node-a#3] Execution created.")]
        [InlineData("node-a", null, "[wf:execution.created] [node:node-a] Execution created.")]
        public void RenderedPrefixes_MatchTheLineContract(string? nodeId, int? runIndex, string expected)
        {
            var (factory, sink) = BuildGenesisLikeFactory();
            using var _ = factory;
            var log = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(Execution());

            if (nodeId is null)
                log.Info(ExecutionLogStages.ExecutionCreated, "Execution created.");
            else
                log.ForNode(nodeId, runIndex).Info(ExecutionLogStages.ExecutionCreated, "Execution created.");

            var rendered = sink.Events.Single().RenderMessage();
            rendered.Should().Be(expected);
            ExecutionLogLineParser.TryParse(rendered, out var parsed).Should().BeTrue();
            parsed!.Stage.Should().Be(ExecutionLogStages.ExecutionCreated);
            parsed.NodeId.Should().Be(nodeId);
            parsed.RunIndex.Should().Be(runIndex);
            parsed.Text.Should().Be("Execution created.");
        }

        [Fact]
        public void NullTraceId_WritesNothing()
        {
            var (factory, sink) = BuildGenesisLikeFactory();
            using var _ = factory;
            var log = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(Execution(traceId: null));

            log.Info(ExecutionLogStages.ExecutionCreated, "Execution created.");
            log.ForNode("n1", 1).Error(ExecutionLogStages.NodeFailed, "Node failed.");

            sink.Events.Should().BeEmpty();
        }

        [Fact]
        public void ThrowingLogger_DoesNotPropagate()
        {
            var logger = new Mock<ILogger<WorkflowExecutionLogger>>();
            logger.Setup(l => l.BeginScope(It.IsAny<Dictionary<string, object>>())).Throws(new InvalidOperationException("boom"));
            var log = new WorkflowExecutionLogger(logger.Object).For(Execution());

            var act = () => log.ForNode("n1", 1).Warn(ExecutionLogStages.NodeWaiting, "Waiting.");

            act.Should().NotThrow();
        }

        [Fact]
        public void PerItemRequestLog_CapsAtFiftyItems_WithOneSummaryLine()
        {
            var (factory, sink) = BuildGenesisLikeFactory();
            using var _ = factory;
            var node = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(Execution()).ForNode("n1", 1);
            var requests = PerItemRequestLog.ForHttp(node, total: 120);

            for (var i = 0; i < 120; i++)
            {
                requests.Sending(i);
                if (i % 2 == 0) requests.Response(i, 200, 12); else requests.Failed(i, "HttpRequestException");
            }

            var lines = sink.Events.Select(e => e.RenderMessage()).ToList();
            lines.Should().HaveCount(50 * 2 + 1);
            lines[0].Should().Be("[wf:node.http.sending] [node:n1#1] Sending request 1 of 120.");
            lines[1].Should().Be("[wf:node.http.response] [node:n1#1] Request 1 of 120: HTTP 200 in 12 ms.");
            lines[3].Should().Be("[wf:node.http.error] [node:n1#1] Request 2 of 120 failed (HttpRequestException).");
            lines.Last().Should().Be("[wf:node.http.notLogged] [node:n1#1] … 70 more request(s) not logged individually.");
        }

        [Fact]
        public void NullNodeLog_IsANoOp()
        {
            var act = () => NodeExecutionLog.Null.Info(ExecutionLogStages.NodeStarted, "x {Count}", 1);
            act.Should().NotThrow();
        }

        [Fact]
        public void EveryStageConstant_MatchesTheStageGroupOfTheLineRegex()
        {
            var stages = typeof(ExecutionLogStages)
                .GetFields()
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            stages.Should().NotBeEmpty();
            foreach (var stage in stages)
            {
                ExecutionLogLineParser.TryParse($"[wf:{stage}] text", out var parsed).Should().BeTrue(stage);
                parsed!.Stage.Should().Be(stage);
            }
        }
    }
}
