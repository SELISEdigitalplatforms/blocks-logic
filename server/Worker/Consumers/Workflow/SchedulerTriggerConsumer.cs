using System.Text.Json;
using Blocks.Genesis;
using DomainService.Workflow.Nodes.TriggerScheduleV1;
using DomainService.Workflow.Services;
using Scheduler.DomainService.Models;

namespace Worker.Consumers.Workflow
{
    /// <summary>
    /// Consumes PublishScheduleCommand from the scheduler when a workflow schedule trigger fires,
    /// deserializes the schedule payload JSON and starts the workflow execution.
    /// </summary>
    public class SchedulerTriggerConsumer : IConsumer<PublishScheduleCommand>
    {
        private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly ILogger<SchedulerTriggerConsumer> _logger;
        private readonly IWorkflowExecutionService _workflowExecutionService;

        public SchedulerTriggerConsumer(
            ILogger<SchedulerTriggerConsumer> logger,
            IWorkflowExecutionService workflowExecutionService)
        {
            _logger = logger;
            _workflowExecutionService = workflowExecutionService;
        }

        public async Task Consume(PublishScheduleCommand @event)
        {
            _logger.LogInformation("SchedulerTriggerConsumer: Received schedule trigger for schedule {ItemId}", @event.ItemId);

            if (string.IsNullOrWhiteSpace(@event.Payload))
            {
                _logger.LogWarning("SchedulerTriggerConsumer: Schedule {ItemId} has no payload; skipping.", @event.ItemId);
                return;
            }

            SchedulerTriggerPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<SchedulerTriggerPayload>(@event.Payload, PayloadSerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "SchedulerTriggerConsumer: Failed to deserialize payload for schedule {ItemId}", @event.ItemId);
                return;
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.WorkflowId) || string.IsNullOrWhiteSpace(payload.TenantId))
            {
                _logger.LogWarning("SchedulerTriggerConsumer: Payload for schedule {ItemId} is missing workflowId/tenantId; skipping.", @event.ItemId);
                return;
            }

            payload.FiredAt ??= DateTime.UtcNow;

            await _workflowExecutionService.SchedulerTriggerStartAsync(payload);
        }
    }
}
