using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Proxy.DomainService.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes;

namespace XUnitTest.Workflow
{
    /// <summary>The stage lines <see cref="NodeExecutorBase{TParameters}.RunAsync"/> writes for every node type.</summary>
    public class NodeExecutorBaseStageLogTests
    {
        private sealed class CollectingSink : ILogEventSink
        {
            public List<LogEvent> Events { get; } = new();
            public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        }

        public sealed class Params
        {
            public string Url { get; set; } = string.Empty;
        }

        private sealed class Executor : NodeExecutorBase<Params>
        {
            public bool Fail { get; init; }
            public override string NodeType => "httpRequest";
            public override string Version => "v1";

            protected override Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, Params? parameters)
                => Task.FromResult(Fail
                    ? NodeExecutionResult.Failed("upstream said zz-marker-4711")
                    : NodeExecutionResult.Successful(new List<NodeOutputItem> { new() { Branch = "source", Data = new NodeOutputItemData() } }));
        }

        private sealed class Resolver : IProxyVariableResolver
        {
            public bool ResolveNothing { get; init; }

            public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
                IReadOnlyCollection<string> names, string tenantId, CancellationToken ct = default)
            {
                IReadOnlyDictionary<string, string> values = ResolveNothing
                    ? new Dictionary<string, string>()
                    : names.ToDictionary(n => n, _ => "secret-value");
                return Task.FromResult(values);
            }
        }

        private static (NodeExecutionContext ctx, CollectingSink sink, SerilogLoggerFactory factory) Build(
            BsonDocument parameters, IProxyVariableResolver? resolver)
        {
            var sink = new CollectingSink();
            var factory = new SerilogLoggerFactory(
                new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger(),
                dispose: true);
            var execution = new WorkflowExecutionEntity
            {
                Id = "exec-1",
                TenantId = "tenant-1",
                WorkflowId = "wf-1",
                WorkflowName = "wf",
                WorkflowSnapshot = new WorkflowEntity { ItemId = "wf-1", Name = "wf", TenantId = "tenant-1" },
                TriggerMetadata = new TriggerMetadata(),
                TraceId = "0af7651916cd43dd8448eb211c80319c",
            };
            var services = new ServiceCollection();
            if (resolver != null) services.AddSingleton(resolver);

            var ctx = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = parameters,
                InputItems = new List<WorkflowItemExecutionEntity>(),
                IterationCount = 0,
                WorkflowContext = new BsonDocument(),
                ServiceProvider = services.BuildServiceProvider(),
                Log = new WorkflowExecutionLogger(factory.CreateLogger<WorkflowExecutionLogger>()).For(execution).ForNode("n1", 1),
            };
            return (ctx, sink, factory);
        }

        private static List<string> Rendered(CollectingSink sink) => sink.Events.Select(e => e.RenderMessage()).ToList();

        [Fact]
        public async Task HealthyRun_WritesTheUniformMiddleSection_WithoutVariableNamesOrValues()
        {
            var (ctx, sink, factory) = Build(new BsonDocument("url", "https://x/{{$VAR.crm_api_key}}?q=zz-marker-4711"), new Resolver());
            using var _ = factory;

            await new Executor().RunAsync(ctx);

            var lines = Rendered(sink);
            lines.Should().Equal(
                "[wf:node.variables] [node:n1#1] Resolving 1 configuration variable(s).",
                "[wf:node.variablesResolved] [node:n1#1] Configuration variables resolved.",
                "[wf:node.parameters] [node:n1#1] Parameters loaded.",
                "[wf:node.executing] [node:n1#1] Running httpRequest logic on 0 item(s).",
                "[wf:node.executed] [node:n1#1] Logic finished: 1 output item(s).");
            lines.Should().NotContain(l => l.Contains("crm_api_key") || l.Contains("zz-marker-4711") || l.Contains("secret-value"));
        }

        [Fact]
        public async Task UnresolvedVariables_LogCountOnly()
        {
            var (ctx, sink, factory) = Build(new BsonDocument("url", "{{$VAR.crm_api_key}}"), new Resolver { ResolveNothing = true });
            using var _ = factory;

            var result = await new Executor().RunAsync(ctx);

            result.IsSuccess.Should().BeFalse();
            var lines = Rendered(sink);
            lines.Last().Should().Be("[wf:node.variablesFailed] [node:n1#1] 1 configuration variable(s) could not be resolved.");
            lines.Should().NotContain(l => l.Contains("crm_api_key"));
            sink.Events.Last().Level.Should().Be(LogEventLevel.Error);
        }

        [Fact]
        public async Task MissingResolver_IsLogged()
        {
            var (ctx, sink, factory) = Build(new BsonDocument("url", "{{$VAR.crm_api_key}}"), resolver: null);
            using var _ = factory;

            await new Executor().RunAsync(ctx);

            Rendered(sink).Last().Should().Be("[wf:node.variablesFailed] [node:n1#1] No variable resolver is available.");
        }

        [Fact]
        public async Task FailureResult_LogsWithoutTheErrorMessage()
        {
            var (ctx, sink, factory) = Build(new BsonDocument("url", "x"), new Resolver());
            using var _ = factory;

            await new Executor { Fail = true }.RunAsync(ctx);

            var lines = Rendered(sink);
            lines.Last().Should().Be("[wf:node.executed] [node:n1#1] Logic reported a failure.");
            lines.Should().NotContain(l => l.Contains("zz-marker-4711"));
            sink.Events.Should().OnlyContain(e => e.Exception == null);
        }
    }
}
