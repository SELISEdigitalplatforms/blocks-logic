using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.DomainService.Dtos;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Events;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;

namespace XUnitTest.Workflow
{
    /// <summary>
    /// Resume continues a failed production run from where it stopped: only the nodes still pending run
    /// again, a failed attempt's partial items never reach the next node, and it can start only once.
    /// </summary>
    public class ResumeExecutionTests
    {
        private static NodeExecutionEntity Row(string id, string nodeId, NodeExecutionStatus status, int items = 0) => new()
        {
            Id = id, NodeId = nodeId, NodeName = nodeId, NodeType = "x", NodeVersion = "v1", Status = status, OutputItemCount = items,
        };

        private static WorkflowExecutionEntity Failed(params string[] active) => new()
        {
            Id = "exec-1",
            TenantId = "t1",
            WorkflowId = "wf",
            WorkflowName = "wf",
            WorkflowSnapshot = new WorkflowEntity { TenantId = "t1" },
            TriggerMetadata = new TriggerMetadata(),
            ExecutionMode = WorkflowExecutionMode.Production,
            Status = WorkflowExecutionStatus.Failed,
            ActiveNodeIds = active.ToList(),
            NodeExecutions =
            [
                Row("r1", "trigger", NodeExecutionStatus.Completed),
                Row("r2", "charge", NodeExecutionStatus.Failed, items: 2),
            ],
        };

        [Fact]
        public void The_nodes_still_pending_are_resumed_and_completed_ones_are_not()
        {
            var execution = Failed("charge", "mail", "trigger", "charge");
            WorkflowEngineService.NodesToResume(execution).Should().Equal("charge", "mail");
        }

        [Fact]
        public void A_failed_attempts_items_are_not_a_completed_output()
        {
            var execution = Failed("charge");
            execution.NodeExecutions.Add(Row("r3", "charge", NodeExecutionStatus.Completed));

            WorkflowEngineService.CompletedNodeExecutionIds(execution).Should().BeEquivalentTo(["r1", "r3"]);
        }

        private static (WorkflowExecutionService Service, Mock<IWorkflowExecutionRepository> Repo, Mock<IMessageClient> Messages) Service(
            WorkflowExecutionEntity execution, bool reopens = true)
        {
            var repo = new Mock<IWorkflowExecutionRepository>();
            repo.Setup(r => r.GetByIdAsync("exec-1", "t1")).ReturnsAsync(execution);
            repo.Setup(r => r.TryReopenFailedExecutionAsync("exec-1", "t1")).ReturnsAsync(reopens);
            var messages = new Mock<IMessageClient>();
            var service = new WorkflowExecutionService(
                Mock.Of<IWorkflowRepository>(), repo.Object, messages.Object, Mock.Of<ILogger<WorkflowExecutionService>>(),
                Mock.Of<IWorkflowEngineService>(), Mock.Of<IWorkflowVersionRepository>(), Mock.Of<IWorkflowNotificationService>(),
                Mock.Of<IWorkflowAuthService>(), Mock.Of<IHttpContextAccessor>(), Mock.Of<IDelegationGrantFactory>(),
                new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance));
            return (service, repo, messages);
        }

        [Fact]
        public async Task A_failed_run_continues_on_its_own_queue_from_the_pending_nodes()
        {
            var (service, _, messages) = Service(Failed("charge", "mail"));

            var result = await service.ResumeExecutionAsync("t1", new WorkflowExecutionGetRequestDto { ExecutionId = "exec-1" });

            result.IsSuccess.Should().BeTrue();
            result.ResumedNodeIds.Should().Equal("charge", "mail");
            messages.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<AddExcuationNodeEvent>>(c =>
                c.ConsumerName == LogicConstants.NodeQueueFor("exec-1") && c.Payload.WorkflowExecutionId == "exec-1")), Times.Exactly(2));
        }

        [Fact]
        public async Task A_second_resume_does_nothing()
        {
            var (service, _, messages) = Service(Failed("charge"), reopens: false);

            var result = await service.ResumeExecutionAsync("t1", new WorkflowExecutionGetRequestDto { ExecutionId = "exec-1" });

            result.IsSuccess.Should().BeFalse();
            messages.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<AddExcuationNodeEvent>>()), Times.Never);
        }

        [Theory]
        [InlineData(WorkflowExecutionMode.Test, WorkflowExecutionStatus.Failed)]
        [InlineData(WorkflowExecutionMode.Production, WorkflowExecutionStatus.Completed)]
        [InlineData(WorkflowExecutionMode.Production, WorkflowExecutionStatus.Running)]
        public async Task Only_a_failed_production_run_can_be_resumed(WorkflowExecutionMode mode, WorkflowExecutionStatus status)
        {
            var execution = Failed("charge");
            execution.ExecutionMode = mode;
            execution.Status = status;
            var (service, repo, _) = Service(execution);

            (await service.ResumeExecutionAsync("t1", new WorkflowExecutionGetRequestDto { ExecutionId = "exec-1" })).IsSuccess.Should().BeFalse();
            repo.Verify(r => r.TryReopenFailedExecutionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
