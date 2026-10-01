using FluentAssertions;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    public class WebhookLastNodeSelectorTests
    {
        [Fact]
        public void Select_ReportedIfGraph_ReturnsCode3NotTheLaterEmptyBranch()
        {
            var executions = new[]
            {
                Node("webhook", 1, branches: new() { ["source"] = 1 }),
                Node("subscription", 2, branches: new() { ["source"] = 1 }),
                Node("checkSub", 3, branches: new() { ["if-false"] = 1, ["if-true"] = 0 }),
                Node("allowed", 4, branches: new() { ["source"] = 1 }),
                Node("getUsers", 5, branches: new() { ["source"] = 1 }),
                Node("mapCounts", 6, branches: new() { ["source"] = 1 }),
                Node("checkAllowed", 7, branches: new() { ["if-false"] = 1, ["if-true"] = 0 }),
                Node("createUser", 8, input: 0, output: 0),
                Node("code3", 9, branches: new() { ["source"] = 1 }),
                Node("notSubscribed", 10, input: 0, output: 0),
            };
            var edges = new[]
            {
                Edge("webhook", "subscription", "source"),
                Edge("subscription", "checkSub", "source"),
                Edge("checkSub", "allowed", "if-false"),
                Edge("checkSub", "notSubscribed", "if-true"),
                Edge("allowed", "getUsers", "source"),
                Edge("getUsers", "mapCounts", "source"),
                Edge("mapCounts", "checkAllowed", "source"),
                Edge("checkAllowed", "createUser", "if-true"),
                Edge("checkAllowed", "code3", "if-false"),
            };

            var selected = WebhookLastNodeSelector.Select("webhook", executions, edges);

            selected.Should().NotBeNull();
            selected!.NodeId.Should().Be("code3");
        }

        [Fact]
        public void Select_LinearGraph_ReturnsTheDownstreamEnd()
        {
            var executions = new[]
            {
                Node("trigger", 1, branches: new() { ["source"] = 1 }),
                Node("a", 2, branches: new() { ["source"] = 1 }),
                Node("b", 3, branches: new() { ["source"] = 1 }),
            };
            var edges = new[]
            {
                Edge("trigger", "a", "source"),
                Edge("a", "b", "source"),
            };

            WebhookLastNodeSelector.Select("trigger", executions, edges)!.NodeId.Should().Be("b");
        }

        [Fact]
        public void Select_TriggerOnly_ReturnsTheTrigger()
        {
            var executions = new[]
            {
                Node("trigger", 1, branches: new() { ["source"] = 1 }),
            };

            WebhookLastNodeSelector.Select("trigger", executions, Array.Empty<EdgeEnity>())!.NodeId
                .Should().Be("trigger");
        }

        [Fact]
        public void Select_BothIfBranchesProduceOutput_ReturnsTheTerminalThatFinishedLast()
        {
            var executions = new[]
            {
                Node("check", 1, branches: new() { ["if-true"] = 1, ["if-false"] = 1 }),
                Node("yes", 2, branches: new() { ["source"] = 1 }),
                Node("no", 5, branches: new() { ["source"] = 1 }),
            };
            var edges = new[]
            {
                Edge("check", "yes", "if-true"),
                Edge("check", "no", "if-false"),
            };

            WebhookLastNodeSelector.Select("check", executions, edges)!.NodeId.Should().Be("no");
        }

        [Fact]
        public void Select_EmptyTerminalOnTheTakenPath_ReturnsThatTerminalNotTheUpstreamNode()
        {
            var executions = new[]
            {
                Node("check", 1, branches: new() { ["if-false"] = 1, ["if-true"] = 0 }),
                Node("filter", 2, input: 1, output: 0),
                Node("unused", 4, input: 0, output: 0),
            };
            var edges = new[]
            {
                Edge("check", "filter", "if-false"),
                Edge("check", "unused", "if-true"),
            };

            WebhookLastNodeSelector.Select("check", executions, edges)!.NodeId.Should().Be("filter");
        }

        [Fact]
        public void Select_FailedNode_DoesNotCrossItAndIgnoresALaterUnusedBranch()
        {
            var executions = new[]
            {
                Node("trigger", 1, branches: new() { ["source"] = 1, ["if-true"] = 0 }),
                Node("failed", 2, status: NodeExecutionStatus.Failed, branches: new() { ["source"] = 1 }),
                Node("child", 3, branches: new() { ["source"] = 1 }),
                Node("unused", 4, input: 0, output: 0),
            };
            var edges = new[]
            {
                Edge("trigger", "failed", "source"),
                Edge("trigger", "unused", "if-true"),
                Edge("failed", "child", "source"),
            };

            WebhookLastNodeSelector.Select("trigger", executions, edges)!.NodeId.Should().Be("failed");
        }

        [Fact]
        public void Select_MergeWithOneLiveParent_ReturnsTheMergeNode()
        {
            var executions = new[]
            {
                Node("trigger", 1, branches: new() { ["if-true"] = 1, ["if-false"] = 0 }),
                Node("live", 2, branches: new() { ["source"] = 1 }),
                Node("dead", 3, input: 0, output: 0),
                Node("merge", 4, branches: new() { ["source"] = 1 }),
            };
            var edges = new[]
            {
                Edge("trigger", "live", "if-true"),
                Edge("trigger", "dead", "if-false"),
                Edge("live", "merge", "source"),
                Edge("dead", "merge", "source"),
            };

            WebhookLastNodeSelector.Select("trigger", executions, edges)!.NodeId.Should().Be("merge");
        }

        [Fact]
        public void Select_BranchKey_UsesIfFalseNotTheDisplayName()
        {
            var executions = new[]
            {
                Node("check", 1, branches: new() { ["if-false"] = 1, ["False"] = 0, ["True"] = 1 }),
                Node("code3", 2, branches: new() { ["source"] = 1 }),
                Node("other", 3, branches: new() { ["source"] = 1 }),
            };
            var edges = new[]
            {
                Edge("check", "code3", "if-false"),
                Edge("check", "other", "if-true"),
            };

            WebhookLastNodeSelector.Select("check", executions, edges)!.NodeId.Should().Be("code3");
        }

        [Fact]
        public void Select_BlankSourceHandle_MatchesSourceBranch()
        {
            var executions = new[]
            {
                Node("trigger", 1, branches: new() { ["source"] = 1 }),
                Node("next", 2, branches: new() { ["source"] = 1 }),
            };
            var edges = new[]
            {
                Edge("trigger", "next", "   "),
            };

            WebhookLastNodeSelector.Select("trigger", executions, edges)!.NodeId.Should().Be("next");
        }

        [Fact]
        public void Select_DuplicateNodeRows_UsesTheHighestRunIndexCounts()
        {
            var executions = new[]
            {
                Node("check", 1, branches: new() { ["if-true"] = 1 }),
                Node("check", 2, branches: new() { ["if-false"] = 1 }),
                Node("yes", 3, branches: new() { ["source"] = 1 }),
                Node("no", 4, branches: new() { ["source"] = 1 }),
            };
            var edges = new[]
            {
                Edge("check", "yes", "if-true"),
                Edge("check", "no", "if-false"),
            };

            WebhookLastNodeSelector.Select("check", executions, edges)!.NodeId.Should().Be("no");
        }

        [Fact]
        public void Select_MissingTriggerRow_FallsBackToHighestRunIndex()
        {
            var executions = new[]
            {
                Node("a", 1, branches: new() { ["source"] = 1 }),
                Node("b", 4, branches: new() { ["source"] = 1 }),
            };

            WebhookLastNodeSelector.Select("missing", executions, Array.Empty<EdgeEnity>())!.NodeId
                .Should().Be("b");
        }

        [Fact]
        public void Select_NoRows_ReturnsNull()
        {
            WebhookLastNodeSelector.Select("trigger", Array.Empty<NodeExecutionEntity>(), Array.Empty<EdgeEnity>())
                .Should().BeNull();
        }

        private static NodeExecutionEntity Node(
            string nodeId,
            int runIndex,
            NodeExecutionStatus status = NodeExecutionStatus.Completed,
            int input = 1,
            int output = 1,
            Dictionary<string, int>? branches = null)
        {
            return new NodeExecutionEntity
            {
                Id = nodeId + "-" + runIndex,
                NodeId = nodeId,
                NodeName = nodeId,
                NodeType = "test",
                NodeVersion = "v1",
                RunIndex = runIndex,
                Status = status,
                InputItemCount = input,
                OutputItemCount = output,
                OutputCountsByBranch = branches ?? new Dictionary<string, int>(),
            };
        }

        private static EdgeEnity Edge(string source, string target, string sourceHandle)
        {
            return new EdgeEnity
            {
                Id = source + "-" + target + "-" + sourceHandle,
                Source = source,
                Target = target,
                SourceHandle = sourceHandle,
                TargetHandle = "target",
            };
        }
    }
}
