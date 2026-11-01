using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;

namespace Workflow.DomainService.BlocksEvents
{
    /// <summary>
    /// Starts every published workflow of the message's tenant that listens to a forwarded Blocks event
    /// (blocks-logic#375): one production execution per workflow, from its published snapshot, once per EventId.
    /// </summary>
    public interface IBlocksEventTriggerService
    {
        Task StartAsync(BlocksEventTriggerMessage message);
    }

    public sealed class BlocksEventTriggerService : IBlocksEventTriggerService
    {
        private readonly IWorkflowRepository _workflowRepository;
        private readonly IWorkflowVersionRepository _workflowVersionRepository;
        private readonly IWorkflowExecutionRepository _executionRepository;
        private readonly IWorkflowExecutionService _executionService;
        private readonly IBlocksEventCatalogService _catalog;
        private readonly ILogger<BlocksEventTriggerService> _logger;

        public BlocksEventTriggerService(
            IWorkflowRepository workflowRepository,
            IWorkflowVersionRepository workflowVersionRepository,
            IWorkflowExecutionRepository executionRepository,
            IWorkflowExecutionService executionService,
            IBlocksEventCatalogService catalog,
            ILogger<BlocksEventTriggerService> logger)
        {
            _workflowRepository = workflowRepository;
            _workflowVersionRepository = workflowVersionRepository;
            _executionRepository = executionRepository;
            _executionService = executionService;
            _catalog = catalog;
            _logger = logger;
        }

        public async Task StartAsync(BlocksEventTriggerMessage message)
        {
            // The tenant comes only from the message's bus context, never from the payload. Nothing from the
            // payload is logged: it is another service's data.
            var tenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogError("TenantId is null or empty in BlocksContext; Blocks Event {EventName} dropped", message.EventName);
                return;
            }

            // Defense in depth: the event may have been switched off after Genesis forwarded it.
            if (!await _catalog.IsEnabledForWorkflowAsync(tenantId, message.EventName))
            {
                _logger.LogInformation("Blocks Event {EventName} is not enabled for workflows; no executions started", message.EventName);
                return;
            }

            var publishedWorkflows = await _workflowRepository.GetPublishWorkflowsByBlocksEventAsync(tenantId, message.EventName);
            if (publishedWorkflows.Count == 0)
            {
                _logger.LogInformation("No published workflow uses Blocks Event {EventName}", message.EventName);
                return;
            }

            // The published version snapshots, exactly as the Data Trigger resolves them: never the draft graph.
            var publishedWorkflowsId = publishedWorkflows.Where(item => item.IsPublished).Select(w => w.ItemId).ToArray();
            var versions = await _workflowVersionRepository.GetWorkflowVersionsAsync(tenantId, publishedWorkflowsId);
            var publishedVersionIds = publishedWorkflows.Select(item => item.PublishedVersionId).ToList();
            var workflows = versions.Where(v => publishedVersionIds.Contains(v.ItemId)).Select(item => item.Snapshot).ToList();

            _logger.LogInformation("Found {Count} workflows for Blocks Event {EventName}", workflows.Count, message.EventName);

            var triggerData = BuildTriggerData(message);
            foreach (var workflow in workflows)
            {
                await QueueAsync(workflow, message, triggerData, tenantId);
            }
        }

        private async Task QueueAsync(WorkflowEntity workflow, BlocksEventTriggerMessage message, BsonArray triggerData, string tenantId)
        {
            try
            {
                // One execution per workflow: the first matching Blocks Event node, like the Data Trigger.
                var triggerNode = workflow.Nodes.FirstOrDefault(n => IsTriggerFor(n, message.EventName));
                if (triggerNode == null)
                {
                    _logger.LogWarning("No Blocks Event trigger node found for WorkflowId: {WorkflowId} with Event: {EventName}",
                        workflow.ItemId, message.EventName);
                    return;
                }

                // A redelivered message (same EventId) must not start the workflow twice.
                if (!string.IsNullOrEmpty(message.EventId)
                    && await _executionRepository.ExistsForBlocksEventAsync(tenantId, workflow.ItemId, message.EventId))
                {
                    _logger.LogInformation("Skipped WorkflowId: {WorkflowId}: Blocks Event {EventId} already started it",
                        workflow.ItemId, message.EventId);
                    return;
                }

                // Sent on the consumer's own flow, so the message client stamps the inbound delegation grant
                // (the triggering user's) on the node message; it is kept by decision (SPEC3 A13).
                await _executionService.QueueTriggeredExecutionAsync(workflow, triggerNode, triggerData, WorkflowExecutionMode.Production);
            }
            catch (Exception ex)
            {
                // Isolates this workflow: the others still start (the queueing itself logs its own failures).
                _logger.LogError(ex, "Failed to start WorkflowId: {WorkflowId} (Blocks Event)", workflow.ItemId);
            }
        }

        private static bool IsTriggerFor(NodeEntity node, string eventName) =>
            node.Type == LogicConstants.BlocksEventNodeType
            && node.Category == LogicConstants.TriggerCategory
            && node.Parameters != null
            && node.Parameters.Contains(EventNameParameter)
            && node.Parameters[EventNameParameter].ToString() == eventName;

        private const string EventNameParameter = "eventName";

        /// <summary>
        /// The trigger node's one output item: the event's metadata and its payload. A payload that is not a
        /// JSON object (an array, a scalar, invalid JSON) is passed on as the raw string.
        /// </summary>
        public static BsonArray BuildTriggerData(BlocksEventTriggerMessage message)
        {
            BsonValue payload;
            try
            {
                payload = BsonDocument.Parse(message.PayloadJson);
            }
            catch (Exception)
            {
                payload = new BsonString(message.PayloadJson ?? string.Empty);
            }

            return new BsonArray
            {
                new BsonDocument
                {
                    { "EventId", message.EventId },
                    { "EventName", message.EventName },
                    { "SourceService", message.SourceService },
                    { "ConsumerName", message.ConsumerName },
                    { "IsMassConsumer", message.IsMassConsumer },
                    { "Timestamp", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc).ToString("o") },
                    { "Payload", payload },
                }
            };
        }
    }
}
