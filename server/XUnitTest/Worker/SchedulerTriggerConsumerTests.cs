using System.Text.Json;
using Blocks.Genesis;
using DomainService.Workflow.Nodes.TriggerScheduleV1;
using DomainService.Workflow.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Models;
using Worker.Consumers.Workflow;

namespace XUnitTest.Worker
{
    /// <summary>
    /// Unit tests for <see cref="SchedulerTriggerConsumer"/> payload deserialization.
    /// The schedule payload is a raw JSON string on PublishScheduleCommand.Payload; the
    /// consumer must deserialize it into SchedulerTriggerPayload regardless of the
    /// message-client serialization pipeline (which previously double-encoded it).
    /// </summary>
    public class SchedulerTriggerConsumerTests
    {
        private readonly Mock<IWorkflowExecutionService> _workflowExecutionService = new();
        private readonly SchedulerTriggerConsumer _consumer;

        public SchedulerTriggerConsumerTests()
        {
            _consumer = new SchedulerTriggerConsumer(
                Mock.Of<ILogger<SchedulerTriggerConsumer>>(),
                _workflowExecutionService.Object);
        }

        private static PublishScheduleCommand RoundTrip(string payloadJson)
        {
            // Simulate the wire: serialize the command (Payload is a string, so the raw JSON
            // survives as a single encoded value) and deserialize it back, mirroring what the
            // consumer receives after queue transport.
            var command = new PublishScheduleCommand { ItemId = "sched-1", Payload = payloadJson };
            var wire = JsonSerializer.Serialize(command);
            return JsonSerializer.Deserialize<PublishScheduleCommand>(wire)!;
        }

        [Fact]
        public async Task Consume_CamelCasePayloadString_DeserializesAndStartsWorkflow()
        {
            var payloadJson =
                "{\"workflowId\":\"fad0a4b4ff1544199aa0adcdc77ca43f\",\"triggerId\":\"27a9d1b4c5584f809864eb5347175bc4\"," +
                "\"tenantId\":\"D68c6aee060a54414adb199f0aba8a082\",\"cronExpression\":\"*/2 * * * *\"}";
            var command = RoundTrip(payloadJson);

            await _consumer.Consume(command);

            _workflowExecutionService.Verify(s => s.SchedulerTriggerStartAsync(It.Is<SchedulerTriggerPayload>(p =>
                p.WorkflowId == "fad0a4b4ff1544199aa0adcdc77ca43f" &&
                p.TriggerId == "27a9d1b4c5584f809864eb5347175bc4" &&
                p.TenantId == "D68c6aee060a54414adb199f0aba8a082" &&
                p.CronExpression == "*/2 * * * *" &&
                p.FiredAt != null)), Times.Once);
        }

        [Fact]
        public async Task Consume_EmptyPayload_SkipsWithoutStarting()
        {
            var command = new PublishScheduleCommand { ItemId = "sched-1", Payload = "  " };

            await _consumer.Consume(command);

            _workflowExecutionService.Verify(s => s.SchedulerTriggerStartAsync(It.IsAny<SchedulerTriggerPayload>()), Times.Never);
        }

        [Fact]
        public async Task Consume_InvalidJsonPayload_SkipsWithoutStarting()
        {
            var command = new PublishScheduleCommand { ItemId = "sched-1", Payload = "not-json{" };

            var act = () => _consumer.Consume(command);

            await act.Should().NotThrowAsync();
            _workflowExecutionService.Verify(s => s.SchedulerTriggerStartAsync(It.IsAny<SchedulerTriggerPayload>()), Times.Never);
        }

        [Fact]
        public async Task Consume_MissingWorkflowId_SkipsWithoutStarting()
        {
            var command = new PublishScheduleCommand
            {
                ItemId = "sched-1",
                Payload = "{\"tenantId\":\"t-1\",\"cronExpression\":\"*/2 * * * *\"}",
            };

            await _consumer.Consume(command);

            _workflowExecutionService.Verify(s => s.SchedulerTriggerStartAsync(It.IsAny<SchedulerTriggerPayload>()), Times.Never);
        }
    }
}
