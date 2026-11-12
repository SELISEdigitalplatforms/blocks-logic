using System.Diagnostics;
using System.Net;
using System.Text;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Moq;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.ActionIamUserV2;
using Workflow.DomainService.Services;
using Xunit.Abstractions;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// IAM User §5 harness (C20): a 200-item Assign Roles › Add run (two IAM calls per item, the heaviest
    /// operation) against an IAM stub that answers at once, 5 warm-up runs then 30 measured runs. Pass: the
    /// run-average node overhead per item (run time ÷ 200) has p95 ≤ 50 ms. Runs only with BLOCKS_PERF_TESTS=1:
    /// BLOCKS_PERF_TESTS=1 dotnet test server/XUnitTest/XUnitTest.csproj -c Release
    /// --filter "FullyQualifiedName~ActionIamUserV2PerformanceTests" --logger "console;verbosity=detailed"
    /// </summary>
    public class ActionIamUserV2PerformanceTests(ITestOutputHelper output)
    {
        private const int Items = 200;
        private const int WarmUpRuns = 5;
        private const int MeasuredRuns = 30;
        private const double BudgetMsPerItem = 50;

        private static readonly BsonDocument Parameters = new()
        {
            { "authenticationType", "clientCredential" },
            { "clientId", "client-1" },
            { "operation", "assignRoles" },
            { "userId", "{{ $json.id }}" },
            { "organizationId", "org1" },
            { "roleMode", "add" },
            { "roles", "{{ $json.roles }}" },
        };

        private static List<WorkflowItemExecutionEntity> InputItems() => Enumerable.Range(0, Items).Select(i => new WorkflowItemExecutionEntity
        {
            Id = $"item-{i}",
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            NodeId = "trigger",
            NodeExecutionId = "ne-1",
            NodeName = "Trigger",
            Branch = "source",
            ParentItemIds = new List<string>(),
            AncestorMap = new Dictionary<string, string>(),
            Data = new NodeOutputItemData
            {
                Output = new BsonDocument { { "id", $"u{i}" }, { "roles", new BsonArray { "editor", "approver" } } },
            },
        }).ToList();

        /// <summary>Answers the read with one role and the write with success, without any delay.</summary>
        private sealed class ImmediateIam : HttpMessageHandler
        {
            private static readonly byte[] Read = Encoding.UTF8.GetBytes("""{"data":{"roles":["viewer"]}}""");
            private static readonly byte[] Write = Encoding.UTF8.GetBytes("""{"isSuccess":true}""");

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(request.Method == HttpMethod.Get ? Read : Write),
                });
        }

        private static ActionIamUserV2Node Node()
        {
            var handler = new ImmediateIam();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
            var tokens = new Mock<IClientCredentialTokenService>();
            tokens.Setup(s => s.GetTokenAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("token");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [ActionIamUserV2Node.BaseUrlSetting] = "https://iam.test.local" })
                .Build();
            return new ActionIamUserV2Node(factory.Object, tokens.Object, new Mock<IWorkflowAuthService>().Object, configuration);
        }

        private static async Task<double> TimeOnce(INodeExecutor node, List<WorkflowItemExecutionEntity> items)
        {
            var context = new NodeExecutionContext
            {
                WorkflowExecutionId = "exec-1",
                TenantId = "tenant-1",
                Parameters = Parameters,
                InputItems = items,
                IterationCount = items.Count,
                HasUpstream = true,
                WorkflowContext = new BsonDocument(),
            };
            var watch = Stopwatch.StartNew();
            var result = await node.RunAsync(context);
            watch.Stop();
            if (!result.IsSuccess || result.OutputItems.Any(i => i.IsError))
                throw new InvalidOperationException("The harness run did not succeed for every item.");
            return watch.Elapsed.TotalMilliseconds / items.Count;
        }

        private static double Percentile(List<double> sorted, double p) =>
            sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1)];

        [PerformanceFact]
        public async Task RunAverageOverheadPerItem_P95_IsWithin50Ms()
        {
            output.WriteLine($".NET {Environment.Version}, {Environment.ProcessorCount} logical CPUs, {(Debugger.IsAttached ? "debugger attached" : "no debugger")}");
            INodeExecutor node = Node();
            var items = InputItems();

            for (var i = 0; i < WarmUpRuns; i++) await TimeOnce(node, items);
            var samples = new List<double>();
            for (var i = 0; i < MeasuredRuns; i++) samples.Add(await TimeOnce(node, items));

            var sorted = samples.OrderBy(x => x).ToList();
            var p50 = Percentile(sorted, 0.50);
            var p95 = Percentile(sorted, 0.95);
            output.WriteLine($"IAM User assignRoles/add n={Items}: per-item overhead p50={p50:F4} ms, p95={p95:F4} ms ({sorted.Count} runs)");
            p95.Should().BeLessThanOrEqualTo(BudgetMsPerItem);
        }
    }
}
