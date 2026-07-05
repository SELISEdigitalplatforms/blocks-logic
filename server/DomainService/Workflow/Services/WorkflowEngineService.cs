using Blocks.Genesis;
using DomainService.Workflow.Events;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Enums;
using DomainService.Workflow.Models;
using DomainService.Workflow.Utils;
using Microsoft.Extensions.Logging;
using DomainService.Workflow.Nodes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Services
{
    [ExcludeFromCodeCoverage]
    public class WorkflowEngineService : IWorkflowEngineService
    {
        private readonly IWorkflowExecutionRepository _workflowExecutionRepository;
        private readonly IEnumerable<INodeExecutor> _nodeExecutors;
        private readonly IMessageClient _messageClient;

        private readonly ILogger<WorkflowEngineService> _logger;
        private readonly IWorkflowNotificationService _workflowNotificationService;

        public WorkflowEngineService(
            IWorkflowExecutionRepository workflowExecutionRepository,
            IEnumerable<INodeExecutor> nodeExecutors,
            IMessageClient messageClient,
            ILogger<WorkflowEngineService> logger,
            IWorkflowNotificationService workflowNotificationService
            )
        {
            _workflowExecutionRepository = workflowExecutionRepository;
            _nodeExecutors = nodeExecutors;
            _messageClient = messageClient;
            _logger = logger;
            _workflowNotificationService = workflowNotificationService;
        }

        /// <summary>
        /// Queue mode: executes a single node, dispatches downstream nodes to Service Bus
        /// </summary>
        public async Task RunNodeAsync(AddExcuationNodeEvent dto)
        {
            await ExecuteNodeAsync(dto, DispatchNodesToQueueAsync);
        }

        /// <summary>
        /// Immediate mode: executes a node and all downstream nodes sequentially in-process
        /// </summary>
        public async Task<WorkflowExecutionModel?> RunNodeInProcessAsync(AddExcuationNodeEvent dto)
        {
            await ExecuteNodeAsync(dto, DispatchNodesImmediateAsync);
            return await _workflowExecutionRepository.GetByIdAsync(dto.WorkflowExecutionId, dto.ProjectKey);
        }

        /// <summary>
        /// Core node execution pipeline: prepare → run executor → complete/fail → dispatch next nodes
        /// </summary>
        private async Task ExecuteNodeAsync(
            AddExcuationNodeEvent dto,
            Func<List<AddExcuationNodeEvent>, Task> dispatchNextNodes,
            Func<NodeExecutionContext, NodeModel, NodeExecutionResult, NodeExecutionResult>? postProcessResult = null)
        {

            var prepared = await PrepareNodeForExecutionAsync(dto);
            if (prepared == null) return;

            var (execution, node, nodeExecution, nodeExecutionContext, executor) = prepared.Value;
            var completionNodeId = execution.ExecutionMode == WorkflowExecutionMode.Test ? execution.WorkflowSnapshot.TestMeta.CompletionNodeId : null;
            try
            {
                var result = await executor.RunAsync(nodeExecutionContext);
                if (postProcessResult != null)
                {
                    result = postProcessResult(nodeExecutionContext, node, result);
                }
                if (!result.IsSuccess)
                {
                    await FailNodeExecutionAsync(execution, nodeExecution, new Exception(result.ErrorMessage));
                }
                else
                {
                    var nextEvents = await CompleteNodeExecutionAsync(nodeExecutionContext, execution, node, nodeExecution, result, completionNodeId);
                    await dispatchNextNodes(nextEvents);

                }
            }
            catch (Exception ex)
            {
                await FailNodeExecutionAsync(execution, nodeExecution, ex);
            }
        }

        /// <summary>
        /// Fetches execution, validates status, checks readiness, creates node metadata,
        /// resolves inputs/ancestors, selects executor, and builds execution context.
        /// Returns null if the node should be skipped.
        /// </summary>
        private async Task<(WorkflowExecutionModel execution, NodeModel node, NodeExecutionModel nodeExecution, NodeExecutionContext context, INodeExecutor executor)?> PrepareNodeForExecutionAsync(AddExcuationNodeEvent dto)
        {
            var execution = await _workflowExecutionRepository.GetByIdAsync(dto.WorkflowExecutionId, dto.ProjectKey)
                ?? throw new InvalidOperationException("Workflow execution not found");

            if (execution.Status == WorkflowExecutionStatus.Completed || execution.Status == WorkflowExecutionStatus.Failed) return null;

            var node = execution.WorkflowSnapshot.Nodes.FirstOrDefault(n => n.Id == dto.NodeId)
                ?? throw new InvalidOperationException("Node not found");

            _logger.LogInformation("Executed Node: {Node}", node);

            if (!IsReadyToExecuteNode(execution, node.Id))
            {
                _logger.LogInformation("Node {NodeId} is not ready to execute yet.", node.Id);
                return null;
            }

            _logger.LogInformation("Node {NodeId} is ready to execute.", node.Id);
            _logger.LogInformation("Node Parameters: {Parameters}", node.Parameters);
            // Create node metadata
            var nodeExecution = new NodeExecutionModel
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

            execution.NodeExecutions.Add(nodeExecution);
            execution.Status = WorkflowExecutionStatus.Running;

            // Atomically push NodeExecution to DB (avoids ReplaceOneAsync race)
            await _workflowExecutionRepository.AtomicAddNodeExecutionAsync(execution.Id, execution.TenantId, nodeExecution);
            _logger.LogInformation("Node {NodeId} Updated to Running status.", node.Id);
            // Resolve input items
            var inputItems = await ResolveInputItemsAsync(execution, node);
            _logger.LogInformation("Node {NodeId} Resolved {InputCount} input items.", node.Id, inputItems.Count);

            // Update node execution with input count
            nodeExecution.InputItemCount = inputItems.Count;

            // Resolve ancestor node outputs for expression access
            var ancestorOutputs = await ResolveAncestorNodeOutputsAsync(execution, node.Id);
            _logger.LogInformation("Node {NodeId} Resolved {AncestorCount} ancestor node outputs.", node.Id, ancestorOutputs.Count);

            var executor = _nodeExecutors.First(ne => ne.NodeType == node.Type);
            _logger.LogInformation("Node {NodeId} Using executor {ExecutorName}.", node.Id, executor.GetType().Name);

            // Build execution context
            var nodeExecutionContext = new NodeExecutionContext
            {
                WorkflowExecutionId = dto.WorkflowExecutionId,
                TenantId = execution.TenantId,
                Parameters = node.Parameters,
                InputItems = inputItems,
                WorkflowContext = execution.Context,
                AncestorNodeOutputs = ancestorOutputs,
                IterationCount = inputItems.Count,
            };

            return (execution, node, nodeExecution, nodeExecutionContext, executor);
        }

        /// <summary>
        /// Dispatches next node events to Service Bus for queue-based execution
        /// </summary>
        private async Task DispatchNodesToQueueAsync(List<AddExcuationNodeEvent> nextEvents)
        {
            foreach (var nextEvent in nextEvents)
            {
                await _messageClient.SendToConsumerAsync(
                    new ConsumerMessage<AddExcuationNodeEvent>
                    {
                        ConsumerName = LogicConstants.NodeExecutionQueue,
                        Payload = nextEvent
                    });
            }
        }

        /// <summary>
        /// Dispatches next node events by executing them sequentially in-process
        /// </summary>
        private async Task DispatchNodesImmediateAsync(List<AddExcuationNodeEvent> nextEvents)
        {
            foreach (var nextEvent in nextEvents)
            {
                await RunNodeInProcessAsync(nextEvent);
            }
        }

        /// <summary>
        /// Checks if all parent nodes are completed
        /// </summary>
        private bool IsReadyToExecuteNode(WorkflowExecutionModel execution, string nodeId)
        {
            var incomingEdges = execution.WorkflowSnapshot.Edges
                .Where(e => e.Target == nodeId)
                .ToList();

            // No incoming edges means it's a start node or independent node
            if (incomingEdges.Count == 0)
            {
                _logger.LogInformation("Node {NodeId} has no incoming edges, ready to execute.", nodeId);
                return true;
            }
            _logger.LogInformation("Node {NodeId} has {EdgeCount} incoming edges, checking parent node statuses.", nodeId, incomingEdges.Count);
            return incomingEdges.All(edge =>
                execution.NodeExecutions.Any(ne =>
                    ne.NodeId == edge.Source && ne.Status == NodeExecutionStatus.Completed));
        }

        /// <summary>
        /// Resolve input items for a node (n8n-style)
        /// </summary>
        private async Task<List<WorkflowItemExecutionModel>> ResolveInputItemsAsync(WorkflowExecutionModel execution, NodeModel node)
        {
            if (node.Category == "trigger") return new();

            var incomingEdges = execution.WorkflowSnapshot.Edges.Where(e => e.Target == node.Id).ToList();
            // Fetch parent items from repository
            var parentNodeIds = incomingEdges.Select(e => new Dictionary<string, string> { { "NodeId", e.Source }, { "Branch", e.SourceHandle ?? "main" } }).Distinct().ToList();

            var parentItems = await _workflowExecutionRepository.GetItemsByNodeIdsAsync(
                execution.Id,
                parentNodeIds,
                execution.TenantId);
            return parentItems;
        }

        /// <summary>
        /// Resolve all ancestor node outputs for expression access
        /// This allows nodes to access data from any upstream node by name
        /// </summary>
        private async Task<Dictionary<string, List<WorkflowItemExecutionModel>>> ResolveAncestorNodeOutputsAsync(WorkflowExecutionModel execution, string currentNodeId)
        {


            var result = new Dictionary<string, List<WorkflowItemExecutionModel>>();
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
                incomingEdges.Select(e => (e.Source, e.SourceHandle ?? "main"))
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
                        Branch: e.SourceHandle ?? "main"
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
                if (!result.ContainsKey(nodeItems.NodeName))
                {
                    result[nodeItems.NodeName] = new List<WorkflowItemExecutionModel>();
                }
                result[nodeItems.NodeName].Add(node);

            }
            return result;
        }

        /// <summary>
        /// Node completed successfully: persist items, update metadata, and return next node events
        /// </summary>
        private async Task<List<AddExcuationNodeEvent>> CompleteNodeExecutionAsync(NodeExecutionContext context, WorkflowExecutionModel execution, NodeModel node, NodeExecutionModel nodeExecution, NodeExecutionResult result, string completionNodeId)
        {
            // Persist output items
            var outputItems = new List<WorkflowItemExecutionModel>();
            int index = 0;
            foreach (var output in result.OutputItems)
            {
                var parentIds = output.ParentItemIds;
                // generate ancestor map for expression access in downstream nodes. merge parent ancestor maps and add direct parents.
                // also add self to ancestor map to allow referencing own output in expressions (e.g. for loops)
                var ancestorMap = context.InputItems.Where(i => parentIds.Contains(i.Id))
                    .SelectMany(i =>
                    {
                        var ancestors = i.AncestorMap != null ? i.AncestorMap : new Dictionary<string, string>();
                        ancestors[i.NodeName] = i.Id; // add direct parent
                        return ancestors;
                    })
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);



                var id = Guid.NewGuid().ToString().Replace("-", "");
                outputItems.Add(new WorkflowItemExecutionModel
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
                // Log but don't fail the workflow - items will be recreated on retry
                Console.WriteLine($"Failed to persist items: {ex.Message}");
                throw; // Re-throw to fail the node execution
            }

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
                execution.Status = WorkflowExecutionStatus.Completed;
                execution.FinishedAt = DateTime.UtcNow;
                execution.ActiveNodeIds = [];
                var userIds = execution.WorkflowSnapshot.TestMeta.UserIds;
                await _workflowNotificationService.NotifyWorkflowExecutionEvent(
                    userIds,
                    execution.WorkflowSnapshot, new Dictionary<string, string>
                    {
                        { "Event", "WorkflowCompleted" },
                        { "Status", "Completed" },
                        { "Message", $"Workflow {execution.WorkflowSnapshot.Name} completed successfully." },
                        { "Data", execution.Id }
                    }
                );
                await _workflowExecutionRepository.AtomicFinalizeExecutionAsync(execution.Id, execution.TenantId);
                return [];
            }

            // Determine next nodes
            var nextNodeIds = execution.WorkflowSnapshot.Edges
                .Where(e => e.Source == node.Id)
                .Select(e => e.Target)
                .Distinct()
                .ToList();

            // Atomically update this NodeExecution to Completed in DB
            await _workflowExecutionRepository.AtomicUpdateNodeExecutionCompletedAsync(
                execution.Id, execution.TenantId, nodeExecution.Id,
                outputItems.Count, nodeExecution.OutputCountsByBranch, contextUpdates);

            // Atomically update ActiveNodeIds: remove completed node, add next nodes
            var isWorkflowComplete = await _workflowExecutionRepository.AtomicCompleteNodeAsync(
                execution.Id, execution.TenantId, node.Id, nextNodeIds);


            if (isWorkflowComplete)
            {
                _logger.LogInformation("All active nodes completed. Marking workflow {WorkflowExecutionId} as complete.", execution.Id);
            }

            // Build next node events
            var nextEvents = nextNodeIds.Select(nextNodeId => new AddExcuationNodeEvent
            {
                WorkflowExecutionId = execution.Id!,
                WorkflowId = execution.WorkflowId,
                NodeId = nextNodeId,
                ProjectKey = execution.TenantId
            }).ToList();

            return nextEvents;
        }

        /// <summary>
        /// Node execution failed
        /// </summary>
        private async Task FailNodeExecutionAsync(WorkflowExecutionModel execution, NodeExecutionModel nodeExecution, Exception ex)
        {
            nodeExecution.Status = NodeExecutionStatus.Failed;
            nodeExecution.EndedAt = DateTime.UtcNow;
            nodeExecution.Error = ex.ToString();
            execution.Status = WorkflowExecutionStatus.Failed;
            execution.ErrorMessage = ex.ToString();
            execution.FinishedAt = DateTime.UtcNow;

            // Atomically update NodeExecution to Failed and mark workflow Failed
            await _workflowExecutionRepository.AtomicUpdateNodeExecutionFailedAsync(
                execution.Id, execution.TenantId, nodeExecution.Id, ex.ToString());

            // Atomically remove failed node from active tracking
            await _workflowExecutionRepository.AtomicCompleteNodeAsync(
                execution.Id, execution.TenantId, nodeExecution.NodeId, new List<string>());
        }

        public async Task<WorkflowExecutionModel?> ExecuteStepNodeAsync(string tenantId, string executionId, string triggerNodeId, string targetNodeId, string? sourceExecutionId = null)
        {
            var execution = await _workflowExecutionRepository.GetByIdAsync(executionId, tenantId);
            if (execution == null || execution.WorkflowSnapshot == null) return execution;

            var workflow = execution.WorkflowSnapshot;
            var targetNode = workflow.Nodes.FirstOrDefault(n => n.Id == targetNodeId);
            if (targetNode == null) return execution;

            WorkflowExecutionModel? sourceExecution = null;
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
            var noopDispatch = new Func<List<AddExcuationNodeEvent>, Task>(_ => Task.CompletedTask);

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
                        continue;
                    }

                    cacheEligible = false;
                }

                Func<NodeExecutionContext, NodeModel, NodeExecutionResult, NodeExecutionResult>? hook = null;
                if (node.PinData != null && node.PinData.Count > 0)
                {
                    hook = (ctx, node, result) => NodeExecutionResult.Successful(BuildPinDataOutputItems(ctx, node, result));
                }

                var evt = new AddExcuationNodeEvent
                {
                    ProjectKey = execution.TenantId,
                    WorkflowId = execution.WorkflowId,
                    WorkflowExecutionId = execution.Id,
                    NodeId = node.Id,
                };

                await ExecuteNodeAsync(evt, noopDispatch, hook);

                execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
                if (execution == null) return null;
                if (execution.Status == WorkflowExecutionStatus.Failed) return execution;
            }

            await _workflowExecutionRepository.AtomicFinalizeExecutionAsync(execution.Id, execution.TenantId);
            return await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
        }

        /// <summary>
        /// Returns <paramref name="targetNodeId"/> plus all transitive ancestors, in topological order
        /// (every node appears after all of its parents). Cycle-safe via visited set.
        /// </summary>
        public IEnumerable<NodeModel> GetTopologicalAncestorsAndTarget(WorkflowModel workflow, string targetNodeId)
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
            var ordered = new List<NodeModel>();

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
        private async Task MaterializePinDataAsync(WorkflowExecutionModel execution, NodeModel node)
        {
            var now = DateTime.UtcNow;
            var nodeExecution = new NodeExecutionModel
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

            var parentAncestorMap = new Dictionary<string, string>();
            foreach (var pi in parentItems)
            {
                if (pi.AncestorMap != null)
                    foreach (var kvp in pi.AncestorMap)
                        parentAncestorMap[kvp.Key] = kvp.Value;
                parentAncestorMap[pi.NodeName] = pi.Id;
            }

            var outputItems = new List<WorkflowItemExecutionModel>();
            int index = 0;
            foreach (var pinValue in node.PinData!)
            {
                var id = Guid.NewGuid().ToString().Replace("-", "");
                var ancestorMap = new Dictionary<string, string>(parentAncestorMap)
                {
                    [node.Name] = id
                };

                outputItems.Add(new WorkflowItemExecutionModel
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
            WorkflowExecutionModel execution,
            WorkflowExecutionModel sourceExecution,
            NodeModel node,
            Dictionary<string, string> remap)
        {
            var sourceNodeExec = sourceExecution.NodeExecutions
                .Where(ne => ne.NodeId == node.Id && ne.Status == NodeExecutionStatus.Completed)
                .OrderByDescending(ne => ne.RunIndex)
                .FirstOrDefault();

            if (sourceNodeExec == null) return false;

            var sourceItems = await _workflowExecutionRepository.GetAllItemsByNodeExecutionIdAsync(
                sourceNodeExec.Id, execution.TenantId);

            foreach (var si in sourceItems)
            {
                if (si.ParentItemIds != null)
                {
                    foreach (var pid in si.ParentItemIds)
                    {
                        if (!remap.ContainsKey(pid)) return false;
                    }
                }
                if (si.AncestorMap != null)
                {
                    foreach (var kv in si.AncestorMap)
                    {
                        if (!remap.ContainsKey(kv.Value)) return false;
                    }
                }
            }

            var now = DateTime.UtcNow;
            var newNodeExecution = new NodeExecutionModel
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

            var newItems = new List<WorkflowItemExecutionModel>(sourceItems.Count);
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

                newItems.Add(new WorkflowItemExecutionModel
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

            await _workflowExecutionRepository.AtomicCompleteNodeAsync(
                execution.Id, execution.TenantId, node.Id, nextNodeIds);

            return true;
        }

        private static BsonValue DeepCopyBson(BsonValue value)
        {
            return BsonSerializer.Deserialize<BsonValue>(value.ToBson());
        }

        private static bool NodesAreEquivalent(NodeModel source, NodeModel current)
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

        private static List<NodeOutputItem> BuildPinDataOutputItems(NodeExecutionContext context, NodeModel node, NodeExecutionResult result)
        {
            var items = new List<NodeOutputItem>(node.PinData!.Count);
            var index = 0;
            foreach (var item in result.OutputItems!)
            {
                if (node.PinData[index] == null) continue;
                item.Data.Output = node.PinData[index];
                index++;
            }
            return result.OutputItems;
        }

        private List<NodeModel> GetAncestorNodesAsync(WorkflowModel workflow, string nodeId)
        {
            var ancestor = new List<NodeModel>();
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
