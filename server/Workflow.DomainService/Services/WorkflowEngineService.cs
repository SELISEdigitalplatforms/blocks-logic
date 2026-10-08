using Blocks.Genesis;
using Workflow.DomainService.Events;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Enums;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Utils;
using Microsoft.Extensions.Logging;
using Workflow.DomainService.Nodes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Diagnostics.CodeAnalysis;
using Workflow.DomainService.Logging;

namespace Workflow.DomainService.Services
{
    [ExcludeFromCodeCoverage]
    public class WorkflowEngineService : IWorkflowEngineService
    {
        private readonly IWorkflowExecutionRepository _workflowExecutionRepository;
        private readonly IEnumerable<INodeExecutor> _nodeExecutors;
        private readonly IMessageClient _messageClient;

        private readonly ILogger<WorkflowEngineService> _logger;
        private readonly IWorkflowNotificationService _workflowNotificationService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWorkflowExecutionLogger _executionLogger;

        public WorkflowEngineService(
            IWorkflowExecutionRepository workflowExecutionRepository,
            IEnumerable<INodeExecutor> nodeExecutors,
            IMessageClient messageClient,
            ILogger<WorkflowEngineService> logger,
            IWorkflowNotificationService workflowNotificationService,
            IServiceProvider serviceProvider,
            IWorkflowExecutionLogger executionLogger
            )
        {
            _executionLogger = executionLogger;
            _workflowExecutionRepository = workflowExecutionRepository;
            _nodeExecutors = nodeExecutors;
            _messageClient = messageClient;
            _logger = logger;
            _workflowNotificationService = workflowNotificationService;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Queue mode: executes a single node, dispatches downstream nodes to Service Bus
        /// </summary>
        public async Task RunNodeAsync(AddExcuationNodeEvent dto, CancellationToken stopping = default)
        {
            await ExecuteNodeAsync(dto, DispatchNodesToQueueAsync, stopping: stopping);
        }

        /// <summary>
        /// Why a step stopped when its Worker shut down. Not "failed": it may have done its work (a call
        /// already sent), and with no rollback a re-run could do it twice.
        /// </summary>
        public const string InterruptedMessage =
            "Interrupted: the Worker stopped while this step was running. It may already have done its work. "
            + "Check before running this workflow again.";

        /// <summary>
        /// Immediate mode: executes a node and all downstream nodes sequentially in-process
        /// </summary>
        public async Task<WorkflowExecutionEntity?> RunNodeInProcessAsync(AddExcuationNodeEvent dto)
        {
            await ExecuteNodeAsync(dto, DispatchNodesImmediateAsync);
            return await _workflowExecutionRepository.GetByIdAsync(dto.WorkflowExecutionId, dto.TenantId);
        }

        /// <summary>
        /// Core node execution pipeline: prepare → run executor → complete/fail → dispatch next nodes
        /// </summary>
        private async Task ExecuteNodeAsync(
            AddExcuationNodeEvent dto,
            Func<List<AddExcuationNodeEvent>, NodeExecutionLog, Task> dispatchNextNodes,
            Func<NodeExecutionContext, NodeEntity, NodeExecutionResult, NodeExecutionResult>? postProcessResult = null,
            Func<NodeExecutionContext, NodeEntity, NodeExecutionResult>? runInstead = null,
            CancellationToken stopping = default)
        {
            EnsureTenantId(dto);

            var prepared = await PrepareNodeRowAsync(dto);
            if (prepared == null) return;

            var (execution, node, nodeExecution) = prepared.Value;
            var log = _executionLogger.For(execution).ForNode(node.Id, nodeExecution.RunIndex);
            var completionNodeId = execution.ExecutionMode == WorkflowExecutionMode.Test ? execution.WorkflowSnapshot.TestMeta.CompletionNodeId : null;

            await _workflowNotificationService.NotifyExecutionEventAsync(
                execution,
                nodeExecution,
                eventName: "NodeStarted",
                code: ExecutionEventCodes.NodeExecutionCode(NodeExecutionStatus.Running),
                status: nameof(NodeExecutionStatus.Running),
                data: nodeExecution.Id,
                message: $"Node '{nodeExecution.NodeName}' started executing.");

            // Everything from here on runs against an already-persisted "Running" node execution row, so
            // ANY failure — building the execution context (input/ancestor resolution, executor lookup) as
            // much as the executor itself throwing — must go through FailNodeExecutionAsync. Otherwise the
            // row is left "Running" forever with nothing to ever mark it Failed (this used to be the case
            // for everything built in the old PrepareNodeForExecutionAsync, which ran outside this try).
            NodeExecutionContext? nodeExecutionContext = null;
            try
            {
                nodeExecutionContext = await BuildNodeExecutionContextAsync(dto, execution, node, log);
                // Without this the step could not be stopped: it kept waiting until the process was killed, and
                // the node stayed Running for ever. Only the step's own work sees it; the writes below do not.
                nodeExecutionContext.CancellationToken = stopping;
                var executor = _nodeExecutors.First(ne => ne.NodeType == node.Type);
                _logger.LogInformation("Node {NodeId} Using executor {ExecutorName}.", node.Id, executor.GetType().Name);

                var result = runInstead != null
                    ? runInstead(nodeExecutionContext, node)
                    : await executor.RunAsync(nodeExecutionContext);
                if (postProcessResult != null)
                {
                    result = postProcessResult(nodeExecutionContext, node, result);
                }
                if (!result.IsSuccess)
                {
                    await FailNodeExecutionAsync(execution, node, nodeExecutionContext, nodeExecution, new Exception(result.ErrorMessage), result.OutputItems, "NodeReportedFailure");
                }
                else
                {
                    var nextEvents = await CompleteNodeExecutionAsync(nodeExecutionContext, execution, node, nodeExecution, result, completionNodeId);
                    await dispatchNextNodes(nextEvents, log);

                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                await FailNodeExecutionAsync(execution, node, nodeExecutionContext, nodeExecution,
                    new Exception(InterruptedMessage), outputItems: null, "Interrupted");
            }
            catch (Exception ex)
            {
                await FailNodeExecutionAsync(execution, node, nodeExecutionContext, nodeExecution, ex, outputItems: null, ex.GetType().Name);
            }
        }

        /// <summary>
        /// Payload TenantId is preferred. Messages published before that field existed only have it
        /// on the Genesis envelope (BlocksContext from SecurityContext / ApplicationProperties).
        /// </summary>
        private void EnsureTenantId(AddExcuationNodeEvent dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.TenantId))
            {
                return;
            }

            dto.TenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dto.TenantId))
            {
                _logger.LogError(
                    "AddExcuationNodeEvent is missing TenantId and BlocksContext has none. WorkflowExecutionId={WorkflowExecutionId} NodeId={NodeId}",
                    dto.WorkflowExecutionId, dto.NodeId);
                throw new InvalidOperationException("TenantId is required to execute a workflow node.");
            }
        }

        /// <summary>
        /// Fetches execution, validates status, checks readiness, and creates + persists the node's
        /// "Running" execution row. Returns null if the node should be skipped. Deliberately does nothing
        /// past the point a node execution row could plausibly exist in the DB: anything riskier (input /
        /// ancestor resolution, executor lookup) lives in <see cref="BuildNodeExecutionContextAsync"/>, which
        /// runs inside <see cref="ExecuteNodeAsync"/>'s try/catch so a failure there still marks the node
        /// Failed instead of leaving it orphaned at Running.
        /// </summary>
        private async Task<(WorkflowExecutionEntity execution, NodeEntity node, NodeExecutionEntity nodeExecution)?> PrepareNodeRowAsync(AddExcuationNodeEvent dto)
        {
            var execution = await _workflowExecutionRepository.GetByIdAsync(dto.WorkflowExecutionId, dto.TenantId)
                ?? throw new InvalidOperationException("Workflow execution not found");

            var executionLog = _executionLogger.For(execution);

            if (execution.Status == WorkflowExecutionStatus.Completed || execution.Status == WorkflowExecutionStatus.Failed)
            {
                executionLog.ForNode(dto.NodeId, runIndex: null).Warn(
                    ExecutionLogStages.NodeSkipped, "Execution already {Status:l}; node event ignored.", execution.Status.ToString());
                return null;
            }

            var node = execution.WorkflowSnapshot.Nodes.FirstOrDefault(n => n.Id == dto.NodeId)
                ?? throw new InvalidOperationException("Node not found");

            if (!IsReadyToExecuteNode(execution, node.Id))
            {
                _logger.LogInformation("Node {NodeId} is not ready to execute yet.", node.Id);
                var (done, total) = CountCompletedParents(execution, node.Id);
                executionLog.ForNode(node.Id, runIndex: null).Info(
                    ExecutionLogStages.NodeWaiting, "Waiting for upstream nodes: {Done} of {Total} complete.", done, total);
                return null;
            }

            // A production node runs once per execution. A second delivery of its message (the Worker
            // stopped mid-node, or the broker lock was lost) must not run it again: there is no rollback,
            // so a second run repeats its side effects (a charge, an email). The atomic add below is the
            // real guard; this check only skips the write when the row is already visible.
            var production = execution.ExecutionMode == WorkflowExecutionMode.Production;
            if (production && HasRunOrIsRunning(execution, node.Id))
            {
                LogDuplicateDelivery(executionLog, node.Id);
                return null;
            }

            _logger.LogInformation("Node {NodeId} is ready to execute.", node.Id);
            // Create node metadata
            var nodeExecution = new NodeExecutionEntity
            {
                Id = Guid.NewGuid().ToString().Replace("-", ""),
                NodeId = node.Id,
                NodeName = node.Name,
                NodeType = node.Type,
                NodeVersion = node.Version,
                Status = NodeExecutionStatus.Running,
                StartedAt = DateTime.UtcNow,
                RunIndex = execution.NodeExecutions.Count + 1
            };

            // Atomically push NodeExecution to DB (avoids ReplaceOneAsync race)
            if (production)
            {
                if (!await _workflowExecutionRepository.TryAddFirstNodeExecutionAsync(execution.Id, execution.TenantId, nodeExecution))
                {
                    LogDuplicateDelivery(executionLog, node.Id);
                    return null;
                }
            }
            else if (!await _workflowExecutionRepository.TryAddNodeExecutionAsync(execution.Id, execution.TenantId, nodeExecution))
            {
                // Another branch failed the execution after it was read above: this node must not run,
                // and the execution must not be set back to Running.
                executionLog.ForNode(node.Id, runIndex: null).Warn(
                    ExecutionLogStages.NodeSkipped, "Execution already Failed; node event ignored.");
                return null;
            }

            execution.NodeExecutions.Add(nodeExecution);
            execution.Status = WorkflowExecutionStatus.Running;
            _logger.LogInformation("Node {NodeId} Updated to Running status.", node.Id);
            executionLog.ForNode(node.Id, nodeExecution.RunIndex).Info(
                ExecutionLogStages.NodeStarted, "Node '{NodeName:l}' ({NodeType:l} v{NodeVersion:l}) started.",
                node.Name, node.Type, FormatVersion(node.Version));

            return (execution, node, nodeExecution);
        }

        /// <summary>
        /// This execution already has a Running or Completed row for the node: its message was delivered
        /// again. Failed rows do not count — a failed node fails the execution, which is checked first.
        /// </summary>
        public static bool HasRunOrIsRunning(WorkflowExecutionEntity execution, string nodeId) =>
            execution.NodeExecutions.Any(ne => ne.NodeId == nodeId
                && ne.Status is NodeExecutionStatus.Running or NodeExecutionStatus.Completed);

        private void LogDuplicateDelivery(ExecutionLog executionLog, string nodeId)
        {
            _logger.LogWarning("Node {NodeId} already ran or is running in this execution; repeated message ignored.", nodeId);
            executionLog.ForNode(nodeId, runIndex: null).Warn(
                ExecutionLogStages.NodeSkipped, "Node already ran or is running; repeated message ignored so it does not run twice.");
        }

        /// <summary>
        /// Resolves input items/ancestor outputs and builds the executor's <see cref="NodeExecutionContext"/>.
        /// Called inside <see cref="ExecuteNodeAsync"/>'s try/catch (unlike the old combined
        /// PrepareNodeForExecutionAsync) so a failure here fails the node instead of orphaning it at Running.
        /// </summary>
        private async Task<NodeExecutionContext> BuildNodeExecutionContextAsync(AddExcuationNodeEvent dto, WorkflowExecutionEntity execution, NodeEntity node, NodeExecutionLog log)
        {
            // Resolve input items
            var inputItems = await ResolveInputItemsAsync(execution, node);
            _logger.LogInformation("Node {NodeId} Resolved {InputCount} input items.", node.Id, inputItems.Count);

            var incomingEdges = execution.WorkflowSnapshot.Edges.Where(e => e.Target == node.Id).ToList();
            var hasUpstream = incomingEdges.Count > 0;
            if (node.Category == "trigger")
            {
                log.Info(ExecutionLogStages.NodeInput, "Trigger node; input comes from the trigger event.");
            }
            else
            {
                log.Info(ExecutionLogStages.NodeInput, "Received {Count} input item(s) from {Parents} upstream node(s).",
                    inputItems.Count, incomingEdges.Select(e => e.Source).Distinct().Count());
                if (inputItems.Count == 0 && hasUpstream)
                {
                    log.Warn(ExecutionLogStages.NodeNoInput, "No input items; this branch was not taken.");
                }
            }

            var nodeExecution = execution.NodeExecutions.Last(ne => ne.NodeId == node.Id);
            // Update node execution with input count
            nodeExecution.InputItemCount = inputItems.Count;

            // Resolve ancestor node outputs for expression access
            var ancestorOutputs = await ResolveAncestorNodeOutputsAsync(execution, node.Id);
            _logger.LogInformation("Node {NodeId} Resolved {AncestorCount} ancestor node outputs.", node.Id, ancestorOutputs.Count);
            log.Info(ExecutionLogStages.NodeAncestors, "{Count} upstream node output(s) available to expressions.", ancestorOutputs.Count);

            // Resumed execution: what this node saved on its last failed attempt, so it need not redo that work.
            var previousAttempt = execution.NodeExecutions.LastOrDefault(ne =>
                ne.NodeId == node.Id && ne.Status == NodeExecutionStatus.Failed && ne.OutputItemCount > 0);
            var previousAttemptItems = previousAttempt is null
                ? new List<WorkflowItemExecutionEntity>()
                : await _workflowExecutionRepository.GetAllItemsByNodeExecutionIdAsync(previousAttempt.Id, execution.TenantId) ?? [];

            // Build execution context
            return new NodeExecutionContext
            {
                WorkflowExecutionId = dto.WorkflowExecutionId,
                WorkflowId = execution.WorkflowId,
                NodeId = node.Id,
                TenantId = execution.TenantId,
                Parameters = (BsonDocument)node.Parameters.DeepClone(),
                InputItems = inputItems,
                WorkflowContext = execution.Context,
                AncestorNodeOutputs = ancestorOutputs,
                IterationCount = inputItems.Count,
                HasUpstream = hasUpstream,
                ServiceProvider = _serviceProvider,
                Log = log,
                PreviousAttemptItems = previousAttemptItems,
            };
        }

        /// <summary>Ids of node-execution rows that completed.</summary>
        public static HashSet<string> CompletedNodeExecutionIds(WorkflowExecutionEntity execution) =>
            execution.NodeExecutions.Where(ne => ne.Status == NodeExecutionStatus.Completed).Select(ne => ne.Id).ToHashSet();

        /// <summary>
        /// The nodes a failed execution must run again to continue: those still pending when it failed
        /// (the failed node, and any branch step that was stopped), minus any that completed.
        /// </summary>
        public static List<string> NodesToResume(WorkflowExecutionEntity execution)
        {
            var completed = execution.NodeExecutions
                .GroupBy(ne => ne.NodeId)
                .Where(g => g.Last().Status == NodeExecutionStatus.Completed)
                .Select(g => g.Key)
                .ToHashSet();
            return execution.ActiveNodeIds.Where(id => !string.IsNullOrWhiteSpace(id) && !completed.Contains(id)).Distinct().ToList();
        }

        /// <summary>
        /// Dispatches next node events to Service Bus for queue-based execution
        /// </summary>
        private async Task DispatchNodesToQueueAsync(List<AddExcuationNodeEvent> nextEvents, NodeExecutionLog log)
        {
            foreach (var nextEvent in nextEvents)
            {
                await _messageClient.SendToConsumerAsync(
                    new ConsumerMessage<AddExcuationNodeEvent>
                    {
                        ConsumerName = LogicConstants.NodeQueueFor(nextEvent.WorkflowExecutionId),
                        Payload = nextEvent
                    });
            }

            if (nextEvents.Count > 0)
            {
                log.Info(ExecutionLogStages.NodeDispatched, "Queued {Count} downstream node(s).", nextEvents.Count);
            }
        }

        /// <summary>
        /// Dispatches next node events by executing them sequentially in-process
        /// </summary>
        private async Task DispatchNodesImmediateAsync(List<AddExcuationNodeEvent> nextEvents, NodeExecutionLog log)
        {
            if (nextEvents.Count > 0)
            {
                log.Info(ExecutionLogStages.NodeDispatchedInProcess, "Running {Count} downstream node(s) in-process.", nextEvents.Count);
            }

            foreach (var nextEvent in nextEvents)
            {
                await RunNodeInProcessAsync(nextEvent);
            }
        }

        /// <summary>
        /// A node is ready when every incoming parent has finished, or cannot still run.
        /// An untaken If branch never starts, so it must not block a join.
        /// </summary>
        private bool IsReadyToExecuteNode(WorkflowExecutionEntity execution, string nodeId)
        {
            var incomingCount = execution.WorkflowSnapshot.Edges.Count(e => e.Target == nodeId);
            if (incomingCount == 0)
            {
                _logger.LogInformation("Node {NodeId} has no incoming edges, ready to execute.", nodeId);
            }
            else
            {
                _logger.LogInformation("Node {NodeId} has {EdgeCount} incoming edges, checking parent node statuses.", nodeId, incomingCount);
            }

            return WorkflowBranchRouting.IsReady(
                nodeId,
                execution.WorkflowSnapshot.Edges,
                execution.NodeExecutions,
                execution.ActiveNodeIds);
        }

        /// <summary>How many of a node's incoming edges already have a completed source node, out of all of them.</summary>
        private static (int Done, int Total) CountCompletedParents(WorkflowExecutionEntity execution, string nodeId)
        {
            var incomingEdges = execution.WorkflowSnapshot.Edges.Where(e => e.Target == nodeId).ToList();
            var done = incomingEdges.Count(edge =>
                execution.NodeExecutions.Any(ne => ne.NodeId == edge.Source && ne.Status == NodeExecutionStatus.Completed));
            return (done, incomingEdges.Count);
        }

        /// <summary>Node versions are stored as "v1" or "1.0"; stage lines add the "v" themselves.</summary>
        private static string FormatVersion(string? version)
        {
            if (string.IsNullOrEmpty(version)) return "?";
            return version.StartsWith('v') || version.StartsWith('V') ? version[1..] : version;
        }

        /// <summary>Per-branch item counts as "true=2, false=1" (or "none"). Branch handles only, never data.</summary>
        private static string FormatBranches(IEnumerable<WorkflowItemExecutionEntity> items)
        {
            var parts = items
                .GroupBy(i => i.Branch)
                .Select(g => $"{g.Key}={g.Count()}")
                .ToList();
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        private static long ElapsedMs(DateTime startedAtUtc)
            => (long)Math.Round((DateTime.UtcNow - startedAtUtc).TotalMilliseconds);

        /// <summary>
        /// Resolve input items for a node (n8n-style)
        /// </summary>
        private async Task<List<WorkflowItemExecutionEntity>> ResolveInputItemsAsync(WorkflowExecutionEntity execution, NodeEntity node)
        {
            if (node.Category == "trigger") return new();

            var incomingEdges = execution.WorkflowSnapshot.Edges.Where(e => e.Target == node.Id).ToList();
            // Fetch parent items from repository
            var parentNodeIds = incomingEdges
                .Select(e => new Dictionary<string, string> { { "NodeId", e.Source }, { "Branch", ResolveEdgeBranch(e.SourceHandle) } })
                .Distinct()
                .ToList();

            var parentItems = await _workflowExecutionRepository.GetItemsByNodeIdsAsync(
                execution.Id,
                parentNodeIds,
                execution.TenantId);
            // Only items of completed attempts: a resumed execution also holds the partial items a parent saved
            // on its failed attempt, and those must not reach this node a second time.
            var completed = CompletedNodeExecutionIds(execution);
            return parentItems.Where(i => completed.Contains(i.NodeExecutionId)).ToList();
        }

        /// <summary>
        /// Resolve all ancestor node outputs for expression access
        /// This allows nodes to access data from any upstream node by name
        /// </summary>
        private async Task<Dictionary<string, List<WorkflowItemExecutionEntity>>> ResolveAncestorNodeOutputsAsync(WorkflowExecutionEntity execution, string currentNodeId)
        {


            var result = new Dictionary<string, List<WorkflowItemExecutionEntity>>();
            var incomingEdges = execution.WorkflowSnapshot.Edges
                      .Where(e => e.Target == currentNodeId)
                      .ToList();
            if (!incomingEdges.Any())
            {
                return result;
            }

            // i want traverse all ancestor nodes not only direct parents
            var allAncestors = new List<Dictionary<string, string>>();
            var visited = new HashSet<string>();

            // seed with direct incoming edges
            var queue = new Queue<(string NodeId, string Branch)>(
                incomingEdges.Select(e => (e.Source, ResolveEdgeBranch(e.SourceHandle)))
            );

            while (queue.Count > 0)
            {
                var (currentNode, branch) = queue.Dequeue();

                // prevent cycles / duplicates
                if (!visited.Add($"{currentNode}:{branch}"))
                    continue;

                allAncestors.Add(new Dictionary<string, string>
    {
        { "NodeId", currentNode },
        { "Branch", branch }
    });

                // find parents of current node
                var parents = execution.WorkflowSnapshot.Edges
                    .Where(e => e.Target == currentNode)
                    .Select(e => (
                        NodeId: e.Source,
                        Branch: ResolveEdgeBranch(e.SourceHandle)
                    ));

                foreach (var p in parents)
                {
                    queue.Enqueue(p);
                }
            }


            // Fetch all ancestor items from repository
            var execuatedItems = await _workflowExecutionRepository.GetItemsByNodeIdsAsync(
                execution.Id,
               allAncestors,
                execution.TenantId);

            foreach (var node in execuatedItems)
            {
                var nodeItems = execution.NodeExecutions.FirstOrDefault(ne => ne.Id == node.NodeExecutionId);
                if (nodeItems != null && nodeItems.Status != NodeExecutionStatus.Completed)
                {
                    continue; // a failed attempt's partial items (resumed execution) are not this ancestor's output
                }
                if (nodeItems == null)
                {
                    // No NodeExecution row matches this item's NodeExecutionId (e.g. a re-run ancestor whose
                    // items now point at a stale id). Skip it rather than crash the whole node's context
                    // build — that used to escape uncaught and orphan the current node at "Running" forever.
                    _logger.LogWarning(
                        "Ancestor item {ItemId} references NodeExecutionId {NodeExecutionId} which has no matching NodeExecution entry; skipping.",
                        node.Id, node.NodeExecutionId);
                    continue;
                }
                if (!result.ContainsKey(nodeItems.NodeName))
                {
                    result[nodeItems.NodeName] = new List<WorkflowItemExecutionEntity>();
                }
                result[nodeItems.NodeName].Add(node);

            }
            return result;
        }

        private static string ResolveEdgeBranch(string? sourceHandle)
        {
            return WorkflowBranchRouting.BranchKey(sourceHandle);
        }

        /// <summary>
        /// Direct inputs first, then ancestor items. <see cref="AncestorMapMerger"/> keeps the first
        /// candidate for an id, so a direct input wins when the same item is also loaded as an ancestor.
        /// Ancestor-only ids (for example a Code <c>.all()</c> source that is not the immediate input)
        /// are still found.
        /// </summary>
        private static IEnumerable<WorkflowItemExecutionEntity> LineageCandidates(NodeExecutionContext context)
        {
            if (context.InputItems != null)
            {
                foreach (var item in context.InputItems)
                {
                    if (item != null)
                    {
                        yield return item;
                    }
                }
            }

            if (context.AncestorNodeOutputs == null)
            {
                yield break;
            }

            foreach (var items in context.AncestorNodeOutputs.Values)
            {
                if (items == null)
                {
                    continue;
                }

                foreach (var item in items)
                {
                    if (item != null)
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>
        /// Node completed successfully: persist items, update metadata, and return next node events
        /// </summary>
        private async Task<List<AddExcuationNodeEvent>> CompleteNodeExecutionAsync(NodeExecutionContext context, WorkflowExecutionEntity execution, NodeEntity node, NodeExecutionEntity nodeExecution, NodeExecutionResult result, string completionNodeId)
        {
            // Persist output items
            var outputItems = new List<WorkflowItemExecutionEntity>();
            int index = 0;
            foreach (var output in result.OutputItems)
            {
                var parentIds = output.ParentItemIds ?? new List<string>();
                // Keep a node name only when every parent agrees on the item id. The output always
                // references itself so downstream expressions can read this node's own item.
                var id = Guid.NewGuid().ToString().Replace("-", "");
                var ancestorMap = AncestorMapMerger.Merge(parentIds, LineageCandidates(context), id, node.Name);
                outputItems.Add(new WorkflowItemExecutionEntity
                {
                    Id = id,
                    WorkflowExecutionId = execution.Id,
                    TenantId = execution.TenantId,
                    NodeId = node.Id,
                    NodeExecutionId = nodeExecution.Id,
                    NodeName = node.Name,
                    Branch = output.Branch,
                    ParentItemIds = parentIds,
                    AncestorMap = ancestorMap,
                    Data = output.Data,
                    ItemIndex = index++
                });
            }

            try
            {

                await _workflowExecutionRepository.AddItemsAsync(execution.TenantId, outputItems);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist output items for node {NodeId}.", node.Id);
                throw; // Re-throw to fail the node execution
            }

            context.Log.Info(ExecutionLogStages.NodeOutputSaved, "Saved {Count} output item(s). Branches: {Branches:l}.",
                outputItems.Count, FormatBranches(outputItems));

            // Update node metadata
            nodeExecution.Status = NodeExecutionStatus.Completed;
            nodeExecution.OutputItemCount = outputItems.Count;
            nodeExecution.OutputCountsByBranch = outputItems
                .GroupBy(o => o.Branch)
                .ToDictionary(g => g.Key, g => g.Count());

            nodeExecution.EndedAt = DateTime.UtcNow;

            // Build context updates BsonDocument for atomic merge
            MongoDB.Bson.BsonDocument? contextUpdates = null;
            if (result.ContextUpdates != null && result.ContextUpdates.Any())
            {
                contextUpdates = new MongoDB.Bson.BsonDocument();
                foreach (var kvp in result.ContextUpdates)
                {
                    contextUpdates[kvp.Key] = MongoDB.Bson.BsonValue.Create(kvp.Value);
                    execution.Context[kvp.Key] = kvp.Value;
                }
            }

            if (!string.IsNullOrEmpty(completionNodeId) && node.Id == completionNodeId)
            {
                // Atomically update this NodeExecution to Completed in DB
                await _workflowExecutionRepository.AtomicUpdateNodeExecutionCompletedAsync(
                    execution.Id, execution.TenantId, nodeExecution.Id,
                    outputItems.Count, nodeExecution.OutputCountsByBranch, contextUpdates);
                context.Log.Info(ExecutionLogStages.NodeCompleted, "Node completed in {DurationMs} ms.", ElapsedMs(nodeExecution.StartedAt));
                context.Log.Info(ExecutionLogStages.NodeTargetReached, "Step target reached; stopping.");
                var finalized = await _workflowExecutionRepository.AtomicFinalizeExecutionAsync(execution.Id, execution.TenantId);
                await _workflowNotificationService.NotifyExecutionEventAsync(
                    execution,
                    nodeExecution,
                    eventName: "NodeCompleted",
                    code: ExecutionEventCodes.NodeExecutionCode(NodeExecutionStatus.Completed),
                    status: nameof(NodeExecutionStatus.Completed),
                    data: nodeExecution.Id,
                    message: $"Node '{nodeExecution.NodeName}' completed successfully.");
                // Another branch failed the execution meanwhile: it stays Failed, and nobody is told "completed".
                if (!finalized) return [];
                execution.Status = WorkflowExecutionStatus.Completed;
                execution.FinishedAt = DateTime.UtcNow;
                execution.ActiveNodeIds = [];
                await _workflowNotificationService.NotifyExecutionEventAsync(
                    execution,
                    nodeExecution: null,
                    eventName: "WorkflowCompleted",
                    code: ExecutionEventCodes.WorkflowExecutionCode(WorkflowExecutionStatus.Completed),
                    status: nameof(WorkflowExecutionStatus.Completed),
                    data: execution.Id!,
                    message: $"Workflow '{execution.WorkflowSnapshot.Name}' completed successfully.");
                LogExecutionCompleted(execution);
                return [];
            }

            // Only edges whose branch carried items. An If output of if-false does not start the if-true child.
            var nextNodeIds = WorkflowBranchRouting.TakenTargets(
                execution.WorkflowSnapshot.Edges.Where(e => e.Source == node.Id),
                nodeExecution.OutputCountsByBranch);

            // Atomically update this NodeExecution to Completed in DB
            await _workflowExecutionRepository.AtomicUpdateNodeExecutionCompletedAsync(
                execution.Id, execution.TenantId, nodeExecution.Id,
                outputItems.Count, nodeExecution.OutputCountsByBranch, contextUpdates);
            context.Log.Info(ExecutionLogStages.NodeCompleted, "Node completed in {DurationMs} ms.", ElapsedMs(nodeExecution.StartedAt));

            await _workflowNotificationService.NotifyExecutionEventAsync(
                execution,
                nodeExecution,
                eventName: "NodeCompleted",
                code: ExecutionEventCodes.NodeExecutionCode(NodeExecutionStatus.Completed),
                status: nameof(NodeExecutionStatus.Completed),
                data: nodeExecution.Id,
                message: $"Node '{nodeExecution.NodeName}' completed successfully.");

            // Atomically update ActiveNodeIds: remove completed node, add next nodes
            var isWorkflowComplete = await _workflowExecutionRepository.AtomicCompleteNodeAsync(
                execution.Id, execution.TenantId, node.Id, nextNodeIds);


            if (isWorkflowComplete)
            {
                _logger.LogInformation("All active nodes completed. Marking workflow {WorkflowExecutionId} as complete.", execution.Id);
                await _workflowNotificationService.NotifyExecutionEventAsync(
                    execution,
                    nodeExecution: null,
                    eventName: "WorkflowCompleted",
                    code: ExecutionEventCodes.WorkflowExecutionCode(WorkflowExecutionStatus.Completed),
                    status: nameof(WorkflowExecutionStatus.Completed),
                    data: execution.Id!,
                    message: $"Workflow '{execution.WorkflowSnapshot.Name}' completed successfully.");
                LogExecutionCompleted(execution);
            }

            // Build next node events
            var nextEvents = nextNodeIds.Select(nextNodeId => new AddExcuationNodeEvent
            {
                TenantId = execution.TenantId,
                WorkflowExecutionId = execution.Id!,
                WorkflowId = execution.WorkflowId,
                NodeId = nextNodeId,
            }).ToList();

            return nextEvents;
        }

        /// <summary>
        /// Node execution failed. Persists any output items the executor produced (including a
        /// synthetic error item) so the UI can show what happened, then marks the node and workflow Failed.
        /// </summary>
        private async Task FailNodeExecutionAsync(
            WorkflowExecutionEntity execution,
            NodeEntity node,
            NodeExecutionContext? context,
            NodeExecutionEntity nodeExecution,
            Exception ex,
            List<NodeOutputItem>? outputItems,
            string errorKind)
        {
            var persistedItems = new List<WorkflowItemExecutionEntity>();

            if (outputItems is { Count: > 0 } && context is not null)
            {
                try
                {
                    int index = 0;
                    foreach (var output in outputItems)
                    {
                        var parentIds = output.ParentItemIds ?? new List<string>();
                        var id = Guid.NewGuid().ToString().Replace("-", "");
                        var ancestorMap = AncestorMapMerger.Merge(parentIds, LineageCandidates(context), id, node.Name);
                        persistedItems.Add(new WorkflowItemExecutionEntity
                        {
                            Id = id,
                            WorkflowExecutionId = execution.Id,
                            TenantId = execution.TenantId,
                            NodeId = node.Id,
                            NodeExecutionId = nodeExecution.Id,
                            NodeName = node.Name,
                            Branch = output.Branch,
                            ParentItemIds = parentIds,
                            AncestorMap = ancestorMap,
                            Data = output.Data,
                            ItemIndex = index++
                        });
                    }

                    await _workflowExecutionRepository.AddItemsAsync(execution.TenantId, persistedItems);
                }
                catch (Exception persistEx)
                {
                    _logger.LogWarning(persistEx, "Failed to persist failure output items for node {NodeId}.", node.Id);
                    persistedItems.Clear();
                }
            }

            nodeExecution.Status = NodeExecutionStatus.Failed;
            nodeExecution.EndedAt = DateTime.UtcNow;
            nodeExecution.Error = ex.ToString();
            nodeExecution.OutputItemCount = persistedItems.Count;
            nodeExecution.OutputCountsByBranch = persistedItems
                .GroupBy(o => o.Branch)
                .ToDictionary(g => g.Key, g => g.Count());

            execution.Status = WorkflowExecutionStatus.Failed;
            execution.ErrorMessage = ex.ToString();
            execution.FinishedAt = DateTime.UtcNow;

            await _workflowExecutionRepository.AtomicUpdateNodeExecutionFailedAsync(
                execution.Id, execution.TenantId, nodeExecution.Id, ex.ToString(),
                nodeExecution.OutputItemCount, nodeExecution.OutputCountsByBranch);

            // Never the exception message: the error text is shown on the node itself, not in the logs.
            var executionLog = _executionLogger.For(execution);
            executionLog.ForNode(node.Id, nodeExecution.RunIndex).Error(
                ExecutionLogStages.NodeFailed, "Node failed after {DurationMs} ms ({ErrorKind:l}). See the node's output for details.",
                ElapsedMs(nodeExecution.StartedAt), errorKind);

            await _workflowNotificationService.NotifyExecutionEventAsync(
                execution,
                nodeExecution,
                eventName: "NodeFailed",
                code: ExecutionEventCodes.NodeExecutionCode(NodeExecutionStatus.Failed),
                status: nameof(NodeExecutionStatus.Failed),
                data: nodeExecution.Id,
                message: $"Node '{nodeExecution.NodeName}' failed: {ex.Message}");

            await _workflowNotificationService.NotifyExecutionEventAsync(
                execution,
                nodeExecution: null,
                eventName: "WorkflowFailed",
                code: ExecutionEventCodes.WorkflowExecutionCode(WorkflowExecutionStatus.Failed),
                status: nameof(WorkflowExecutionStatus.Failed),
                data: execution.Id!,
                message: $"Workflow '{execution.WorkflowSnapshot.Name}' failed: {ex.Message}");
            executionLog.Error(ExecutionLogStages.ExecutionFailed, "Execution failed at node '{NodeName:l}'.", node.Name);

            await _workflowExecutionRepository.AtomicCompleteNodeAsync(
                execution.Id, execution.TenantId, nodeExecution.NodeId, new List<string>());
        }

        public async Task<WorkflowExecutionEntity?> ExecuteStepNodeAsync(string tenantId, string executionId, string triggerNodeId, string targetNodeId, string? sourceExecutionId = null)
        {
            var execution = await _workflowExecutionRepository.GetByIdAsync(executionId, tenantId);
            if (execution == null || execution.WorkflowSnapshot == null) return execution;

            var workflow = execution.WorkflowSnapshot;
            var targetNode = workflow.Nodes.FirstOrDefault(n => n.Id == targetNodeId);
            if (targetNode == null) return execution;

            WorkflowExecutionEntity? sourceExecution = null;
            if (!string.IsNullOrEmpty(sourceExecutionId))
            {
                sourceExecution = await _workflowExecutionRepository.GetByIdAsync(sourceExecutionId, tenantId);
                if (sourceExecution != null && sourceExecution.TenantId != execution.TenantId) sourceExecution = null;
                if (sourceExecution != null && sourceExecution.WorkflowId != execution.WorkflowId) sourceExecution = null;
                if (sourceExecution != null && sourceExecution.Status != WorkflowExecutionStatus.Completed) sourceExecution = null;
            }

            var ordered = GetTopologicalAncestorsAndTarget(workflow, targetNodeId).ToList();
            var cacheEligible = sourceExecution != null;
            var remap = new Dictionary<string, string>();
            var noopDispatch = new Func<List<AddExcuationNodeEvent>, NodeExecutionLog, Task>((_, _) => Task.CompletedTask);
            // The target's own CompleteNodeExecutionAsync writes execution.completed, unless it came from the cache.
            var lastNodeFromCache = false;

            for (int i = 0; i < ordered.Count; i++)
            {
                var node = ordered[i];

                if (execution.Status == WorkflowExecutionStatus.Failed) return execution;

                if (cacheEligible)
                {
                    var sourceNodeExec = sourceExecution!.NodeExecutions
                        .FirstOrDefault(ne => ne.NodeId == node.Id);
                    var sourceNode = sourceExecution.WorkflowSnapshot.Nodes
                        .FirstOrDefault(n => n.Id == node.Id);

                    bool cacheValid =
                        sourceNodeExec != null
                        && sourceNode != null
                        && sourceNodeExec.RunIndex == i + 1
                        && NodesAreEquivalent(sourceNode, node);

                    if (cacheValid
                        && await TryMaterializeFromSourceExecutionAsync(execution, sourceExecution!, node, remap))
                    {
                        execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
                        if (execution == null) return null;
                        if (execution.Status == WorkflowExecutionStatus.Failed) return execution;
                        lastNodeFromCache = true;
                        continue;
                    }

                    cacheEligible = false;
                }

                Func<NodeExecutionContext, NodeEntity, NodeExecutionResult, NodeExecutionResult>? hook = null;
                Func<NodeExecutionContext, NodeEntity, NodeExecutionResult>? runInstead = null;
                if (node.PinData != null && node.PinData.Count > 0)
                {
                    if (SkipsRunWhenPinned(node))
                    {
                        // Pinned means "use this data, don't run": an action node (function, proxy, mail,
                        // AI, HTTP) is not called at all, so a step test has no real side effects or cost.
                        runInstead = (ctx, node) =>
                        {
                            ctx.Log.Info(ExecutionLogStages.NodePinned, "Not run; {Count} pinned item(s) used.", node.PinData!.Count);
                            return NodeExecutionResult.Successful(PinnedOutputItems(ctx, node));
                        };
                    }
                    else
                    {
                        // Logic, transform and trigger nodes have no side effects and decide branches, so
                        // they still run and only their output is replaced — but a failure stays a failure.
                        hook = (ctx, node, result) =>
                        {
                            if (!result.IsSuccess) return result;
                            ctx.Log.Info(ExecutionLogStages.NodePinned, "Output replaced with {Count} pinned item(s).", node.PinData!.Count);
                            return NodeExecutionResult.Successful(BuildPinDataOutputItems(ctx, node, result));
                        };
                    }
                }

                var evt = new AddExcuationNodeEvent
                {
                    TenantId = execution.TenantId,
                    WorkflowId = execution.WorkflowId,
                    WorkflowExecutionId = execution.Id,
                    NodeId = node.Id,
                };

                await ExecuteNodeAsync(evt, noopDispatch, hook, runInstead);
                lastNodeFromCache = false;

                execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
                if (execution == null) return null;
                if (execution.Status == WorkflowExecutionStatus.Failed) return execution;
            }

            await _workflowExecutionRepository.AtomicFinalizeExecutionAsync(execution.Id, execution.TenantId);
            execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
            if (execution != null && execution.Status == WorkflowExecutionStatus.Completed)
            {
                await _workflowNotificationService.NotifyExecutionEventAsync(
                    execution,
                    nodeExecution: null,
                    eventName: "WorkflowCompleted",
                    code: ExecutionEventCodes.WorkflowExecutionCode(WorkflowExecutionStatus.Completed),
                    status: nameof(WorkflowExecutionStatus.Completed),
                    data: execution.Id!,
                    message: $"Workflow '{execution.WorkflowSnapshot.Name}' completed successfully.");
                if (lastNodeFromCache)
                {
                    LogExecutionCompleted(execution);
                }
            }
            return execution;
        }

        /// <summary>
        /// Returns <paramref name="targetNodeId"/> plus all transitive ancestors, in topological order
        /// (every node appears after all of its parents). Cycle-safe via visited set.
        /// </summary>
        public IEnumerable<NodeEntity> GetTopologicalAncestorsAndTarget(WorkflowEntity workflow, string targetNodeId)
        {
            var nodesById = workflow.Nodes.ToDictionary(n => n.Id);

            var ancestors = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(targetNodeId);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!ancestors.Add(current)) continue;

                var parentIds = workflow.Edges
                    .Where(e => e.Target == current)
                    .Select(e => e.Source);

                foreach (var parentId in parentIds)
                {
                    if (!ancestors.Contains(parentId))
                        stack.Push(parentId);
                }
            }

            var all = ancestors.Where(id => nodesById.ContainsKey(id)).ToList();

            var inDegree = all.ToDictionary(id => id, _ => 0);
            var adjacency = all.ToDictionary(id => id, _ => new List<string>());

            foreach (var edge in workflow.Edges)
            {
                if (!inDegree.ContainsKey(edge.Target) || !adjacency.ContainsKey(edge.Source)) continue;
                adjacency[edge.Source].Add(edge.Target);
                inDegree[edge.Target]++;
            }

            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var ordered = new List<NodeEntity>();

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (nodesById.TryGetValue(id, out var nodeModel))
                    ordered.Add(nodeModel);

                foreach (var childId in adjacency[id])
                {
                    if (--inDegree[childId] == 0)
                        queue.Enqueue(childId);
                }
            }

            foreach (var id in all)
            {
                if (!ordered.Any(n => n.Id == id) && nodesById.TryGetValue(id, out var n))
                    ordered.Add(n);
            }

            return ordered;
        }

        /// <summary>
        /// Synthesizes a completed NodeExecution and one WorkflowItemExecutionModel per PinData
        /// entry, mirroring CompleteNodeExecutionAsync's persistence sequence.
        /// </summary>
        private async Task MaterializePinDataAsync(WorkflowExecutionEntity execution, NodeEntity node)
        {
            var now = DateTime.UtcNow;
            var nodeExecution = new NodeExecutionEntity
            {
                Id = Guid.NewGuid().ToString().Replace("-", ""),
                NodeId = node.Id,
                NodeName = node.Name,
                NodeType = node.Type,
                NodeVersion = node.Version,
                RunIndex = execution.NodeExecutions.Count + 1,
                Status = NodeExecutionStatus.Running,
                StartedAt = now
            };

            execution.NodeExecutions.Add(nodeExecution);
            execution.Status = WorkflowExecutionStatus.Running;

            await _workflowExecutionRepository.AtomicAddNodeExecutionAsync(execution.Id, execution.TenantId, nodeExecution);

            var directParents = execution.WorkflowSnapshot.Edges
                .Where(e => e.Target == node.Id)
                .Select(e => e.Source)
                .Distinct()
                .ToList();

            var parentItems = await _workflowExecutionRepository.GetItemsByNodeIdsAsync(
                execution.Id,
                directParents.Select(pid => new Dictionary<string, string>
                {
                    { "NodeId", pid },
                    { "Branch", "main" }
                }).ToList(),
                execution.TenantId);

            var parentIds = parentItems.Select(pi => pi.Id).ToList();

            var outputItems = new List<WorkflowItemExecutionEntity>();
            int index = 0;
            foreach (var pinValue in node.PinData!)
            {
                var id = Guid.NewGuid().ToString().Replace("-", "");
                var ancestorMap = AncestorMapMerger.Merge(parentIds, parentItems, id, node.Name);

                outputItems.Add(new WorkflowItemExecutionEntity
                {
                    Id = id,
                    WorkflowExecutionId = execution.Id,
                    TenantId = execution.TenantId,
                    NodeId = node.Id,
                    NodeExecutionId = nodeExecution.Id,
                    NodeName = node.Name,
                    Branch = "main",
                    ParentItemIds = new List<string>(),
                    AncestorMap = ancestorMap,
                    Data = new NodeOutputItemData
                    {
                        Parameters = node.Parameters ?? new BsonDocument(),
                        Input = new BsonDocument(),
                        Output = pinValue
                    },
                    ItemIndex = index++
                });
            }

            await _workflowExecutionRepository.AddItemsAsync(execution.TenantId, outputItems);

            nodeExecution.Status = NodeExecutionStatus.Completed;
            nodeExecution.OutputItemCount = outputItems.Count;
            nodeExecution.OutputCountsByBranch = outputItems
                .GroupBy(o => o.Branch)
                .ToDictionary(g => g.Key, g => g.Count());
            nodeExecution.EndedAt = DateTime.UtcNow;

            var nextNodeIds = execution.WorkflowSnapshot.Edges
                .Where(e => e.Source == node.Id)
                .Select(e => e.Target)
                .Distinct()
                .ToList();

            await _workflowExecutionRepository.AtomicUpdateNodeExecutionCompletedAsync(
                execution.Id, execution.TenantId, nodeExecution.Id,
                outputItems.Count, nodeExecution.OutputCountsByBranch, null);

            await _workflowExecutionRepository.AtomicCompleteNodeAsync(
                execution.Id, execution.TenantId, node.Id, nextNodeIds);
        }

        /// <summary>
        /// Attempts to materialize a Completed source NodeExecution into the current execution.
        /// Returns false if the source has no usable cached execution, or if any source item
        /// references a parent item not yet present in <paramref name="remap"/> (conservative cascade).
        /// On success, persists items + node execution metadata using the same sequence as
        /// <see cref="MaterializePinDataAsync"/> and updates <paramref name="remap"/> with new item ids.
        /// </summary>
        private async Task<bool> TryMaterializeFromSourceExecutionAsync(
            WorkflowExecutionEntity execution,
            WorkflowExecutionEntity sourceExecution,
            NodeEntity node,
            Dictionary<string, string> remap)
        {
            var sourceNodeExec = sourceExecution.NodeExecutions
                .Where(ne => ne.NodeId == node.Id && ne.Status == NodeExecutionStatus.Completed)
                .OrderByDescending(ne => ne.RunIndex)
                .FirstOrDefault();

            if (sourceNodeExec == null) return false;

            var sourceItems = await _workflowExecutionRepository.GetAllItemsByNodeExecutionIdAsync(
                sourceNodeExec.Id, execution.TenantId);


            var now = DateTime.UtcNow;
            var newNodeExecution = new NodeExecutionEntity
            {
                Id = Guid.NewGuid().ToString().Replace("-", ""),
                NodeId = node.Id,
                NodeName = node.Name,
                NodeType = node.Type,
                NodeVersion = sourceNodeExec.NodeVersion,
                RunIndex = execution.NodeExecutions.Count + 1,
                Status = NodeExecutionStatus.Running,
                StartedAt = now,
                OutputCountsByBranch = new Dictionary<string, int>(sourceNodeExec.OutputCountsByBranch)
            };

            execution.NodeExecutions.Add(newNodeExecution);
            execution.Status = WorkflowExecutionStatus.Running;

            await _workflowExecutionRepository.AtomicAddNodeExecutionAsync(
                execution.Id, execution.TenantId, newNodeExecution);

            var log = _executionLogger.For(execution).ForNode(node.Id, newNodeExecution.RunIndex);
            log.Info(ExecutionLogStages.NodeStarted, "Node '{NodeName:l}' ({NodeType:l} v{NodeVersion:l}) started.",
                node.Name, node.Type, FormatVersion(newNodeExecution.NodeVersion));

            await _workflowNotificationService.NotifyExecutionEventAsync(
                execution,
                newNodeExecution,
                eventName: "NodeStarted",
                code: ExecutionEventCodes.NodeExecutionCode(NodeExecutionStatus.Running),
                status: nameof(NodeExecutionStatus.Running),
                data: newNodeExecution.Id,
                message: $"Node '{newNodeExecution.NodeName}' started executing (from cache).");

            var newItems = new List<WorkflowItemExecutionEntity>(sourceItems.Count);
            int index = 0;
            foreach (var si in sourceItems)
            {
                var newId = Guid.NewGuid().ToString().Replace("-", "");
                remap[si.Id] = newId;

                var remappedParents = si.ParentItemIds != null
                    ? si.ParentItemIds.Select(p => remap[p]).ToList()
                    : new List<string>();

                var remappedAncestors = si.AncestorMap != null
                    ? si.AncestorMap.ToDictionary(kv => kv.Key, kv => remap[kv.Value])
                    : new Dictionary<string, string>();

                newItems.Add(new WorkflowItemExecutionEntity
                {
                    Id = newId,
                    WorkflowExecutionId = execution.Id,
                    TenantId = execution.TenantId,
                    NodeId = node.Id,
                    NodeExecutionId = newNodeExecution.Id,
                    NodeName = node.Name,
                    Branch = si.Branch,
                    ParentItemIds = remappedParents,
                    AncestorMap = remappedAncestors,
                    Data = new NodeOutputItemData
                    {
                        Parameters = DeepCopyBson(si.Data.Parameters),
                        Input = DeepCopyBson(si.Data.Input),
                        Output = DeepCopyBson(si.Data.Output)
                    },
                    ItemIndex = index++
                });
            }

            if (newItems.Count > 0)
            {
                await _workflowExecutionRepository.AddItemsAsync(execution.TenantId, newItems);
            }

            newNodeExecution.Status = NodeExecutionStatus.Completed;
            newNodeExecution.OutputItemCount = newItems.Count;
            newNodeExecution.EndedAt = DateTime.UtcNow;

            var nextNodeIds = execution.WorkflowSnapshot.Edges
                .Where(e => e.Source == node.Id)
                .Select(e => e.Target)
                .Distinct()
                .ToList();

            await _workflowExecutionRepository.AtomicUpdateNodeExecutionCompletedAsync(
                execution.Id, execution.TenantId, newNodeExecution.Id,
                newNodeExecution.OutputItemCount, newNodeExecution.OutputCountsByBranch, contextUpdates: null);

            await _workflowNotificationService.NotifyExecutionEventAsync(
                execution,
                newNodeExecution,
                eventName: "NodeCompleted",
                code: ExecutionEventCodes.NodeExecutionCode(NodeExecutionStatus.Completed),
                status: nameof(NodeExecutionStatus.Completed),
                data: newNodeExecution.Id,
                message: $"Node '{newNodeExecution.NodeName}' completed from cache.");

            await _workflowExecutionRepository.AtomicCompleteNodeAsync(
                execution.Id, execution.TenantId, node.Id, nextNodeIds);

            log.Info(ExecutionLogStages.NodeCached, "Reused {Count} output item(s) from execution {SourceExecutionId:l}.",
                newItems.Count, sourceExecution.Id);

            return true;
        }

        private void LogExecutionCompleted(WorkflowExecutionEntity execution)
        {
            _executionLogger.For(execution).Info(
                ExecutionLogStages.ExecutionCompleted, "Execution completed in {DurationMs} ms; {NodeRuns} node run(s).",
                ElapsedMs(execution.StartedAt), execution.NodeExecutions.Count);
        }

        private static BsonValue? DeepCopyBson(BsonValue? value)
        {
            return value?.DeepClone();
        }

        private static bool NodesAreEquivalent(NodeEntity source, NodeEntity current)
        {
            if (!string.Equals(source.Id, current.Id)) return false;
            if (!string.Equals(source.Name, current.Name)) return false;
            if (!string.Equals(source.Category, current.Category)) return false;
            if (!string.Equals(source.Type, current.Type)) return false;
            if (!string.Equals(source.Version, current.Version)) return false;
            if (!EqualsBson(source.Parameters, current.Parameters)) return false;
            if (!EqualsBson(source.Settings, current.Settings)) return false;
            if (!EqualsBson(source.PinData, current.PinData)) return false;
            return true;
        }

        private static bool EqualsBson(BsonValue? a, BsonValue? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }

        private static List<NodeOutputItem> BuildPinDataOutputItems(NodeExecutionContext context, NodeEntity node, NodeExecutionResult result)
        {
            // Item i gets pin entry i; items past the pinned ones keep their real output (this used to
            // index past the end of the pin data and fail the node).
            var outputs = result.OutputItems ?? new List<NodeOutputItem>();
            for (var i = 0; i < outputs.Count && i < node.PinData!.Count; i++)
            {
                if (node.PinData[i] != null && !node.PinData[i].IsBsonNull) outputs[i].Data.Output = node.PinData[i];
            }
            return outputs;
        }

        /// <summary>Action nodes are the ones that act on the outside world; pinned, they are not run.</summary>
        public static bool SkipsRunWhenPinned(NodeEntity node) =>
            string.Equals(node.Category, "action", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// One output item per pin entry, as the node itself would have made them: branch "source", item i
        /// tied to input item i when there is one, so expressions downstream resolve as after a real run.
        /// </summary>
        public static List<NodeOutputItem> PinnedOutputItems(NodeExecutionContext context, NodeEntity node)
        {
            var inputs = context.InputItems ?? new List<WorkflowItemExecutionEntity>();
            var items = new List<NodeOutputItem>(node.PinData!.Count);
            for (var i = 0; i < node.PinData.Count; i++)
            {
                var input = i < inputs.Count ? inputs[i] : null;
                items.Add(new NodeOutputItem
                {
                    Data = new NodeOutputItemData
                    {
                        Input = input?.Data?.Output ?? new BsonDocument(),
                        Output = node.PinData[i],
                        Parameters = node.Parameters ?? new BsonDocument(),
                    },
                    Branch = "source",
                    ParentItemIds = input is null ? new List<string>() : new List<string> { input.Id },
                });
            }
            return items;
        }

        private List<NodeEntity> GetAncestorNodesAsync(WorkflowEntity workflow, string nodeId)
        {
            var ancestor = new List<NodeEntity>();
            var visited = new HashSet<string>();
            var stack = new Stack<string>();

            stack.Push(nodeId);

            while (stack.Count > 0)
            {
                var current = stack.Pop();

                if (!visited.Add(current))
                    continue;

                var parents = workflow.Edges
                    .Where(e => e.Target == current)
                    .Select(e => e.Source);

                foreach (var parentId in parents)
                {
                    var parentNode = workflow.Nodes.FirstOrDefault(n => n.Id == parentId);

                    if (parentNode != null)
                    {
                        ancestor.Add(parentNode);
                    }

                    stack.Push(parentId);
                }
            }

            return ancestor;
        }
    }
}
