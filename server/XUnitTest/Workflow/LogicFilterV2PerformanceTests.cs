using System.Diagnostics;
using FluentAssertions;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Nodes;
using Workflow.DomainService.Nodes.LogicFilterV2;
using Workflow.DomainService.Nodes.LogicIFV1;
using Blocks.Genesis.Entities.Logic;
using Xunit.Abstractions;

namespace XUnitTest.Workflow
{
    /// <summary>Runs only when BLOCKS_PERF_TESTS=1, so CI and coverage runs never assert timings on shared runners.</summary>
    public sealed class PerformanceFactAttribute : FactAttribute
    {
        public const string EnvironmentVariable = "BLOCKS_PERF_TESTS";

        public PerformanceFactAttribute()
        {
            if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
                Skip = $"Performance harness; set {EnvironmentVariable}=1 and run in Release.";
        }
    }

    /// <summary>
    /// SPEC9 §5 harness (H13, C15): If v1 and Filter RunAsync in process, 5 warm-up runs then 30 measured runs
    /// per node, alternating the two. Pass: Filter p95 ≤ 1.10 × If p95 at 1,000 and at 10,000 items.
    /// Run with: BLOCKS_PERF_TESTS=1 dotnet test server/XUnitTest/XUnitTest.csproj -c Release
    /// --filter FullyQualifiedName~LogicFilterV2PerformanceTests --logger "console;verbosity=detailed"
    /// </summary>
    public class LogicFilterV2PerformanceTests(ITestOutputHelper output)
    {
        private const int WarmUpRuns = 5;
        private const int MeasuredRuns = 30;

        // The §5 workload: status equals paid (string) AND total greater_than 100 (number).
        private static readonly BsonDocument Parameters = new()
        {
            { "conditionType", "and" },
            {
                "conditions", new BsonArray
                {
                    new BsonDocument { { "left", "{{ $json.status }}" }, { "operator", "equals" }, { "right", "paid" }, { "type", "string" } },
                    new BsonDocument { { "left", "{{ $json.total }}" }, { "operator", "greater_than" }, { "right", "100" }, { "type", "number" } },
                }
            },
        };

        /// <summary>
        /// { id, status, total } items: 3 in 4 are paid and 2 in 3 have a total above 100, so half of them match.
        /// </summary>
        private static List<WorkflowItemExecutionEntity> Items(int count) => Enumerable.Range(0, count).Select(i => new WorkflowItemExecutionEntity
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
                Output = new BsonDocument
                {
                    { "id", i },
                    { "status", i % 4 == 3 ? "draft" : "paid" },
                    { "total", i % 3 == 0 ? 40 : 120 },
                },
            },
        }).ToList();

        private static NodeExecutionContext Context(List<WorkflowItemExecutionEntity> items) => new()
        {
            WorkflowExecutionId = "exec-1",
            TenantId = "tenant-1",
            Parameters = Parameters,
            InputItems = items,
            IterationCount = items.Count,
            WorkflowContext = new BsonDocument(),
        };

        private static async Task<double> TimeOnce(INodeExecutor executor, List<WorkflowItemExecutionEntity> items)
        {
            var context = Context(items);
            var watch = Stopwatch.StartNew();
            var result = await executor.RunAsync(context);
            watch.Stop();
            if (!result.IsSuccess) throw new InvalidOperationException(result.ErrorMessage);
            return watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Nearest-rank percentile of the sorted samples.</summary>
        private static double Percentile(List<double> sorted, double p) =>
            sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1)];

        /// <summary>Writes p50 and p95 and returns p95.</summary>
        private double Report(string node, int size, List<double> samples)
        {
            var sorted = samples.OrderBy(x => x).ToList();
            var p95 = Percentile(sorted, 0.95);
            output.WriteLine($"{node} n={size}: p50={Percentile(sorted, 0.50):F3} ms, p95={p95:F3} ms ({sorted.Count} runs)");
            return p95;
        }

        private void WriteEnvironment() =>
            output.WriteLine($".NET {Environment.Version}, {Environment.ProcessorCount} logical CPUs, {(Debugger.IsAttached ? "debugger attached" : "no debugger")}");

        /// <summary>If v1 alone: the §5 baseline, first taken before any Filter code existed.</summary>
        [PerformanceFact]
        public async Task IfV1_Baseline()
        {
            WriteEnvironment();
            foreach (var size in new[] { 1_000, 10_000 })
            {
                var items = Items(size);
                var ifNode = new LogicIfV1Node();
                var run = await ifNode.RunAsync(Context(items));
                run.IsSuccess.Should().BeTrue(run.ErrorMessage);
                run.OutputItems.Should().NotBeEmpty($"If v1 routes the n={size} items");
                for (var i = 0; i < WarmUpRuns; i++) await TimeOnce(ifNode, items);
                var samples = new List<double>();
                for (var i = 0; i < MeasuredRuns; i++) samples.Add(await TimeOnce(ifNode, items));
                samples.Should().HaveCount(MeasuredRuns);
                Report("If v1", size, samples).Should().BePositive($"n={size}");
            }
        }

        [PerformanceFact]
        public async Task FilterV2_P95_IsWithin110PercentOfIfV1()
        {
            WriteEnvironment();
            foreach (var size in new[] { 1_000, 10_000 })
            {
                var items = Items(size);
                INodeExecutor ifNode = new LogicIfV1Node(), filterNode = new LogicFilterV2Node();
                for (var i = 0; i < WarmUpRuns; i++)
                {
                    await TimeOnce(ifNode, items);
                    await TimeOnce(filterNode, items);
                }
                List<double> ifSamples = [], filterSamples = [];
                for (var i = 0; i < MeasuredRuns; i++)
                {
                    ifSamples.Add(await TimeOnce(ifNode, items));
                    filterSamples.Add(await TimeOnce(filterNode, items));
                }

                var ifP95 = Report("If v1", size, ifSamples);
                var filterP95 = Report("Filter v2", size, filterSamples);
                output.WriteLine($"n={size}: Filter p95 / If p95 = {filterP95 / ifP95:F3}");
                filterP95.Should().BeLessThanOrEqualTo(1.10 * ifP95, $"n={size}");
            }
        }
    }
}
