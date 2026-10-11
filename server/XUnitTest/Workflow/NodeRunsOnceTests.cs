using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// A production node runs once per execution: a second delivery of its message (Worker stopped
    /// mid-node, broker lock lost) must not run it again, since nothing rolls back its side effects.
    /// And a sync webhook caller is told when the run failed.
    /// </summary>
    public class NodeRunsOnceTests
    {
        private static WorkflowExecutionEntity Execution(params (string NodeId, NodeExecutionStatus Status)[] rows) => new()
        {
            Id = "exec-1",
            TenantId = "t1",
            WorkflowId = "wf-1",
            WorkflowName = "wf",
            WorkflowSnapshot = new WorkflowEntity { TenantId = "t1" },
            TriggerMetadata = new TriggerMetadata(),
            ExecutionMode = WorkflowExecutionMode.Production,
            NodeExecutions = rows.Select((r, i) => new NodeExecutionEntity
            {
                Id = $"ne-{i}",
                NodeId = r.NodeId,
                NodeName = r.NodeId,
                NodeType = "function",
                NodeVersion = "v1",
                Status = r.Status,
            }).ToList(),
        };

        [Theory]
        [InlineData(NodeExecutionStatus.Running, true)]     // the Worker stopped mid-node
        [InlineData(NodeExecutionStatus.Completed, true)]   // stopped after completing, before the ack
        [InlineData(NodeExecutionStatus.Failed, false)]     // the execution is failed then; checked earlier
        [InlineData(NodeExecutionStatus.Pending, false)]
        public void A_node_with_a_running_or_completed_row_is_not_run_again(NodeExecutionStatus status, bool skipped)
        {
            WorkflowEngineService.HasRunOrIsRunning(Execution(("n1", status)), "n1").Should().Be(skipped);
        }

        [Fact]
        public void Another_nodes_row_does_not_count()
        {
            WorkflowEngineService.HasRunOrIsRunning(Execution(("n2", NodeExecutionStatus.Completed)), "n1").Should().BeFalse();
        }

        [Fact]
        public void The_atomic_add_matches_only_while_the_node_has_no_running_or_completed_row()
        {
            var serializer = BsonSerializer.SerializerRegistry.GetSerializer<WorkflowExecutionEntity>();
            var rendered = WorkflowExecutionRepository.FirstRunFilter("exec-1", "n1")
                .Render(new RenderArgs<WorkflowExecutionEntity>(serializer, BsonSerializer.SerializerRegistry));

            Assert.Equal(BsonDocument.Parse(
                "{ _id: 'exec-1', NodeExecutions: { $not: { $elemMatch: { $and: [ { NodeId: 'n1' }, "
                + "{ $or: [ { Status: " + (int)NodeExecutionStatus.Running + " }, { Status: " + (int)NodeExecutionStatus.Completed + " } ] } ] } } } }"), rendered);
        }

        [Fact]
        public void A_failed_in_process_run_answers_failed()
        {
            var execution = Execution();
            execution.Status = WorkflowExecutionStatus.Failed;
            WorkflowExecutionService.WebhookFinalStatus(execution).Should().Be("Failed");

            execution.Status = WorkflowExecutionStatus.Completed;
            WorkflowExecutionService.WebhookFinalStatus(execution).Should().Be("Completed");
            WorkflowExecutionService.WebhookFinalStatus(null).Should().Be("Completed");
        }
    }
}
