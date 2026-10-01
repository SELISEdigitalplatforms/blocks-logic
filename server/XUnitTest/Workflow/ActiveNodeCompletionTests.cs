using FluentAssertions;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    public class ActiveNodeCompletionTests
    {
        [Fact]
        public void Apply_LeafThenSubscriptionIf_LeavesDiditActive()
        {
            var afterLeaf = ActiveNodeCompletion.Apply(
                ["statusUpdate", "subscriptionIf"],
                "statusUpdate",
                [],
                WorkflowExecutionStatus.Running);

            afterLeaf.ActiveNodeIds.Should().Equal("subscriptionIf");
            afterLeaf.Status.Should().Be(WorkflowExecutionStatus.Running);
            afterLeaf.BecameComplete.Should().BeFalse();

            var afterIf = ActiveNodeCompletion.Apply(
                afterLeaf.ActiveNodeIds,
                "subscriptionIf",
                ["didit"],
                afterLeaf.Status);

            afterIf.ActiveNodeIds.Should().Equal("didit");
            afterIf.Status.Should().Be(WorkflowExecutionStatus.Running);
            afterIf.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_SubscriptionIfThenLeaf_LeavesDiditActive()
        {
            var afterIf = ActiveNodeCompletion.Apply(
                ["statusUpdate", "subscriptionIf"],
                "subscriptionIf",
                ["didit"],
                WorkflowExecutionStatus.Running);

            afterIf.ActiveNodeIds.Should().Equal("statusUpdate", "didit");
            afterIf.Status.Should().Be(WorkflowExecutionStatus.Running);
            afterIf.BecameComplete.Should().BeFalse();

            var afterLeaf = ActiveNodeCompletion.Apply(
                afterIf.ActiveNodeIds,
                "statusUpdate",
                [],
                afterIf.Status);

            afterLeaf.ActiveNodeIds.Should().Equal("didit");
            afterLeaf.Status.Should().Be(WorkflowExecutionStatus.Running);
            afterLeaf.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_HttpThenLeaf_LeavesIf7Active()
        {
            var afterHttp = ActiveNodeCompletion.Apply(
                ["statusUpdate", "subscriptionHttp"],
                "subscriptionHttp",
                ["if7"],
                WorkflowExecutionStatus.Running);

            afterHttp.ActiveNodeIds.Should().Equal("statusUpdate", "if7");
            afterHttp.BecameComplete.Should().BeFalse();

            var afterLeaf = ActiveNodeCompletion.Apply(
                afterHttp.ActiveNodeIds,
                "statusUpdate",
                [],
                afterHttp.Status);

            afterLeaf.ActiveNodeIds.Should().Equal("if7");
            afterLeaf.Status.Should().Be(WorkflowExecutionStatus.Running);
            afterLeaf.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_LeafThenHttp_LeavesIf7Active()
        {
            var afterLeaf = ActiveNodeCompletion.Apply(
                ["statusUpdate", "subscriptionHttp"],
                "statusUpdate",
                [],
                WorkflowExecutionStatus.Running);

            afterLeaf.ActiveNodeIds.Should().Equal("subscriptionHttp");
            afterLeaf.BecameComplete.Should().BeFalse();

            var afterHttp = ActiveNodeCompletion.Apply(
                afterLeaf.ActiveNodeIds,
                "subscriptionHttp",
                ["if7"],
                afterLeaf.Status);

            afterHttp.ActiveNodeIds.Should().Equal("if7");
            afterHttp.Status.Should().Be(WorkflowExecutionStatus.Running);
            afterHttp.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_LastLeafWithNoChildren_Completes()
        {
            var result = ActiveNodeCompletion.Apply(
                ["lastLeaf"],
                "lastLeaf",
                [],
                WorkflowExecutionStatus.Running);

            result.ActiveNodeIds.Should().BeEmpty();
            result.Status.Should().Be(WorkflowExecutionStatus.Completed);
            result.BecameComplete.Should().BeTrue();
        }

        [Fact]
        public void Apply_FailedLastNode_StaysFailed()
        {
            var result = ActiveNodeCompletion.Apply(
                ["lastNode"],
                "lastNode",
                [],
                WorkflowExecutionStatus.Failed);

            result.ActiveNodeIds.Should().BeEmpty();
            result.Status.Should().Be(WorkflowExecutionStatus.Failed);
            result.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_AlreadyCompletedEmptySet_DoesNotCompleteAgain()
        {
            var result = ActiveNodeCompletion.Apply(
                [],
                "anything",
                [],
                WorkflowExecutionStatus.Completed);

            result.ActiveNodeIds.Should().BeEmpty();
            result.Status.Should().Be(WorkflowExecutionStatus.Completed);
            result.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_DuplicateAndBlankNextIds_CollapseToOne()
        {
            var result = ActiveNodeCompletion.Apply(
                ["node"],
                "node",
                ["", "  ", "next", "next"],
                WorkflowExecutionStatus.Running);

            result.ActiveNodeIds.Should().Equal("next");
            result.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_NullActiveList_IsAnEmptyList()
        {
            var result = ActiveNodeCompletion.Apply(
                null,
                "gone",
                ["kept"],
                WorkflowExecutionStatus.Running);

            result.ActiveNodeIds.Should().Equal("kept");
            result.Status.Should().Be(WorkflowExecutionStatus.Running);
            result.BecameComplete.Should().BeFalse();
        }

        [Fact]
        public void Apply_FinishedNodeListedAsChild_StaysActive()
        {
            var result = ActiveNodeCompletion.Apply(
                ["parent"],
                "parent",
                ["parent"],
                WorkflowExecutionStatus.Running);

            result.ActiveNodeIds.Should().Equal("parent");
            result.BecameComplete.Should().BeFalse();
        }
    }
}
