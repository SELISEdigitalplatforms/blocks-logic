using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using Workflow.DomainService.Utils;

namespace Workflow.DomainService.Actions
{
    /// <summary>
    /// Starts every published workflow of the message's tenant whose Action trigger names the received action: one
    /// production execution per workflow, from its published snapshot, once per event id.
    /// </summary>
    public interface IActionWorkflowTriggerService
    {
        /// <summary>Never throws for one workflow's failure: the others still start.</summary>
        Task StartAsync(BlocksAction action);
    }

    /// <inheritdoc cref="IActionWorkflowTriggerService"/>
    public sealed class ActionWorkflowTriggerService : IActionWorkflowTriggerService
    {
        /// <summary>The trigger node parameter that holds the action name (kept from the Blocks Event node).</summary>
        public const string ActionNameParameter = "eventName";

        private readonly IWorkflowRepository _workflowRepository;
        private readonly IWorkflowVersionRepository _workflowVersionRepository;
        private readonly IWorkflowExecutionService _executionService;
        private readonly IActionsRepository _actions;
        private readonly IActionStartGuard _startGuard;
        private readonly ILogger<ActionWorkflowTriggerService> _logger;

        public ActionWorkflowTriggerService(
            IWorkflowRepository workflowRepository,
            IWorkflowVersionRepository workflowVersionRepository,
            IWorkflowExecutionService executionService,
            IActionsRepository actions,
            IActionStartGuard startGuard,
            ILogger<ActionWorkflowTriggerService> logger)
        {
            _workflowRepository = workflowRepository;
            _workflowVersionRepository = workflowVersionRepository;
            _executionService = executionService;
            _actions = actions;
            _startGuard = startGuard;
            _logger = logger;
        }

        public async Task StartAsync(BlocksAction action)
        {
            ArgumentNullException.ThrowIfNull(action);

            // The tenant comes only from the message's bus context. Nothing from the data is logged.
            var tenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogError("No tenant in the message context; action {Action} ({EventId}) dropped", action.Action, action.Id);
                return;
            }

            if (string.IsNullOrWhiteSpace(action.Id) || string.IsNullOrWhiteSpace(action.Action))
            {
                _logger.LogWarning("Action without an id or a name dropped in tenant {TenantId}", tenantId);
                return;
            }

            // The switch may have been turned off after Genesis sent it.
            var definition = await _actions.FindAsync(action.Action);
            if (definition is not { Workflow: true })
            {
                _logger.LogInformation("Action {Action} ({EventId}) is not on for workflows in tenant {TenantId}; dropped",
                    action.Action, action.Id, tenantId);
                return;
            }

            var workflows = await PublishedSnapshotsAsync(tenantId, action.Action);
            _logger.LogInformation("Found {Count} published workflows for action {Action} ({EventId})",
                workflows.Count, action.Action, action.Id);

            var triggerItems = ActionTriggerData.ToTriggerItems(action);
            foreach (var workflow in workflows)
            {
                await QueueAsync(workflow, action, triggerItems, tenantId);
            }
        }

        /// <summary>The published version snapshots, exactly as the Data Trigger resolves them: never the draft graph.</summary>
        private async Task<List<WorkflowEntity>> PublishedSnapshotsAsync(string tenantId, string actionName)
        {
            var publishedWorkflows = await _workflowRepository.GetPublishedWorkflowsByActionAsync(tenantId, actionName);
            if (publishedWorkflows.Count == 0) return [];

            var publishedWorkflowIds = publishedWorkflows.Where(item => item.IsPublished).Select(w => w.ItemId).ToArray();
            var versions = await _workflowVersionRepository.GetWorkflowVersionsAsync(tenantId, publishedWorkflowIds);
            var publishedVersionIds = publishedWorkflows.Select(item => item.PublishedVersionId).ToHashSet(StringComparer.Ordinal);
            return versions.Where(v => publishedVersionIds.Contains(v.ItemId)).Select(item => item.Snapshot).ToList();
        }

        private async Task QueueAsync(WorkflowEntity workflow, BlocksAction action, BsonArray triggerItems, string tenantId)
        {
            try
            {
                // One execution per workflow: the first matching Action trigger node, like the Data Trigger.
                var triggerNode = workflow.Nodes.FirstOrDefault(n => IsTriggerFor(n, action.Action));
                if (triggerNode == null)
                {
                    _logger.LogWarning("No Action trigger node found for WorkflowId: {WorkflowId} with action {Action}",
                        workflow.ItemId, action.Action);
                    return;
                }

                await using var claim = await _startGuard.TryClaimAsync(tenantId, workflow.ItemId, action.Id);
                if (claim is null)
                {
                    _logger.LogInformation("Skipped WorkflowId: {WorkflowId}: action event {EventId} already started it",
                        workflow.ItemId, action.Id);
                    return;
                }

                // Sent on the consumer's own flow, so the message client stamps the inbound delegation grant
                // (the acting user's) on the node message.
                await _executionService.QueueTriggeredExecutionAsync(workflow, triggerNode, triggerItems, WorkflowExecutionMode.Production);
            }
            catch (Exception ex)
            {
                // Isolates this workflow: the others still start (the queueing itself logs its own failures).
                _logger.LogError(ex, "Failed to start WorkflowId: {WorkflowId} (action {Action})", workflow.ItemId, action.Action);
            }
        }

        private static bool IsTriggerFor(NodeEntity node, string actionName) =>
            node.Type == LogicConstants.ActionTriggerNodeType
            && node.Category == LogicConstants.TriggerCategory
            && node.Parameters != null
            && node.Parameters.TryGetValue(ActionNameParameter, out var value)
            && value.IsString
            && value.AsString == actionName;
    }
}
