using FluentAssertions;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    public class WorkflowBranchRoutingTests
    {
        [Fact]
        public void TakenTargets_IfFalse_DoesNotStartTheTrueChild()
        {
            var edges = new[]
            {
                Edge("check", "yes", "if-true"),
                Edge("check", "no", "if-false"),
            };

            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>
            {
                ["if-false"] = 1,
                ["if-true"] = 0,
            }).Should().Equal("no");
        }

        [Fact]
        public void TakenTargets_IfTrue_DoesNotStartTheFalseChild()
        {
            var edges = new[]
            {
                Edge("check", "yes", "if-true"),
                Edge("check", "no", "if-false"),
            };

            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>
            {
                ["if-true"] = 1,
                ["if-false"] = 0,
            }).Should().Equal("yes");
        }

        [Fact]
        public void TakenTargets_MixedBatch_StartsBothChildren()
        {
            var edges = new[]
            {
                Edge("check", "yes", "if-true"),
                Edge("check", "no", "if-false"),
            };

            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>
            {
                ["if-true"] = 2,
                ["if-false"] = 1,
            }).Should().Equal("yes", "no");
        }

        [Fact]
        public void TakenTargets_ZeroCountDropsOnlyThatHandle()
        {
            var edges = new[]
            {
                Edge("check", "yes", "if-true"),
                Edge("check", "no", "if-false"),
            };

            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>
            {
                ["if-true"] = 2,
                ["if-false"] = 0,
            }).Should().Equal("yes");
        }

        [Fact]
        public void TakenTargets_BlankHandleMatchesSourceBranch()
        {
            var edges = new[]
            {
                Edge("trigger", "next", "  "),
                Edge("trigger", "unused", "if-true"),
            };

            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>
            {
                ["source"] = 1,
            }).Should().Equal("next");
        }

        [Fact]
        public void TakenTargets_MissingOrEmptyCounts_TakesNoEdges()
        {
            var edges = new[] { Edge("trigger", "next", "source") };

            WorkflowBranchRouting.TakenTargets(edges, null).Should().BeEmpty();
            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>()).Should().BeEmpty();
        }

        [Fact]
        public void TakenTargets_LinearSourceEdge_IsTaken()
        {
            var edges = new[] { Edge("trigger", "a", "source") };

            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>
            {
                ["source"] = 1,
            }).Should().Equal("a");
        }

        [Fact]
        public void TakenTargets_DuplicateTargets_AreReturnedOnce()
        {
            var edges = new[]
            {
                Edge("check", "shared", "if-true"),
                Edge("check", "shared", "if-false"),
            };

            WorkflowBranchRouting.TakenTargets(edges, new Dictionary<string, int>
            {
                ["if-true"] = 1,
                ["if-false"] = 1,
            }).Should().Equal("shared");
        }

        [Fact]
        public void IsReady_JoinWithUnreachableUntakenParent_IsReady()
        {
            var edges = new[]
            {
                Edge("check", "live", "if-false"),
                Edge("check", "dead", "if-true"),
                Edge("live", "merge", "source"),
                Edge("dead", "merge", "source"),
            };
            var executions = new[]
            {
                Node("check", 1, new() { ["if-false"] = 1, ["if-true"] = 0 }),
                Node("live", 2, new() { ["source"] = 1 }),
            };

            WorkflowBranchRouting.IsReady("merge", edges, executions, activeNodeIds: new[] { "merge" })
                .Should().BeTrue();
        }

        [Fact]
        public void IsReady_SiblingStillActive_IsNotReady()
        {
            var edges = new[]
            {
                Edge("live", "merge", "source"),
                Edge("sibling", "merge", "source"),
            };
            var executions = new[]
            {
                Node("live", 1, new() { ["source"] = 1 }),
            };

            WorkflowBranchRouting.IsReady("merge", edges, executions, activeNodeIds: new[] { "sibling" })
                .Should().BeFalse();
        }

        [Fact]
        public void IsReady_SiblingDownstreamOfActiveNode_IsNotReady()
        {
            var edges = new[]
            {
                Edge("active", "sibling", "source"),
                Edge("sibling", "merge", "source"),
                Edge("live", "merge", "source"),
            };
            var executions = new[]
            {
                Node("live", 1, new() { ["source"] = 1 }),
            };

            WorkflowBranchRouting.IsReady("merge", edges, executions, activeNodeIds: new[] { "active" })
                .Should().BeFalse();
        }

        [Fact]
        public void IsReady_SiblingOnlyReachableThroughUntakenIfEdge_IsReady()
        {
            var edges = new[]
            {
                Edge("check", "dead", "if-true"),
                Edge("check", "live", "if-false"),
                Edge("dead", "merge", "source"),
                Edge("live", "merge", "source"),
            };
            var executions = new[]
            {
                Node("check", 1, new() { ["if-true"] = 0, ["if-false"] = 1 }),
                Node("live", 2, new() { ["source"] = 1 }),
            };

            WorkflowBranchRouting.IsReady("merge", edges, executions, activeNodeIds: Array.Empty<string>())
                .Should().BeTrue();
        }

        [Fact]
        public void IsReady_FailedParentDoesNotBlock_AndWalkDoesNotPassThroughIt()
        {
            var edges = new[]
            {
                Edge("upstream", "gate", "source"),
                Edge("gate", "beyond", "source"),
                Edge("beyond", "merge", "source"),
                Edge("live", "merge", "source"),
            };
            var executions = new[]
            {
                Node("live", 1, new() { ["source"] = 1 }),
                Node("gate", 2, new() { ["source"] = 1 }, NodeExecutionStatus.Failed),
            };

            WorkflowBranchRouting.IsReady("merge", edges, executions, activeNodeIds: new[] { "upstream" })
                .Should().BeTrue();
        }

        [Fact]
        public void IsReady_NodeBeingTestedCannotReachItsOwnParents()
        {
            var edges = new[]
            {
                Edge("parent", "merge", "source"),
                Edge("merge", "parent", "source"),
            };

            WorkflowBranchRouting.IsReady("merge", edges, Array.Empty<NodeExecutionEntity>(), activeNodeIds: new[] { "merge" })
                .Should().BeTrue();
        }

        [Fact]
        public void IsReady_WalkReachingTheNodeDoesNotContinueToItsParents()
        {
            var edges = new[]
            {
                Edge("runner", "side", "source"),
                Edge("side", "merge", "source"),
                Edge("merge", "parent", "source"),
                Edge("parent", "merge", "source"),
            };
            var executions = new[]
            {
                Node("side", 1, new() { ["source"] = 1 }),
            };

            WorkflowBranchRouting.IsReady("merge", edges, executions, activeNodeIds: new[] { "runner" })
                .Should().BeTrue();
        }

        [Fact]
        public void IsReady_RunningParent_IsNotReady()
        {
            var edges = new[] { Edge("parent", "child", "source") };
            var executions = new[]
            {
                Node("parent", 1, new() { ["source"] = 1 }, NodeExecutionStatus.Running),
            };

            WorkflowBranchRouting.IsReady("child", edges, executions, activeNodeIds: Array.Empty<string>())
                .Should().BeFalse();
        }

        [Fact]
        public void IsReady_NoIncomingEdges_IsReady()
        {
            WorkflowBranchRouting.IsReady("trigger", Array.Empty<EdgeEnity>(), Array.Empty<NodeExecutionEntity>(), Array.Empty<string>())
                .Should().BeTrue();
        }

        [Fact]
        public void IsReady_LatestRunIndexWins()
        {
            var edges = new[] { Edge("parent", "child", "source") };
            var executions = new[]
            {
                Node("parent", 1, new() { ["source"] = 1 }, NodeExecutionStatus.Running),
                Node("parent", 2, new() { ["source"] = 1 }, NodeExecutionStatus.Completed),
            };

            WorkflowBranchRouting.IsReady("child", edges, executions, activeNodeIds: Array.Empty<string>())
                .Should().BeTrue();
        }

        private static NodeExecutionEntity Node(
            string nodeId,
            int runIndex,
            Dictionary<string, int> branches,
            NodeExecutionStatus status = NodeExecutionStatus.Completed)
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
                OutputCountsByBranch = branches,
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
