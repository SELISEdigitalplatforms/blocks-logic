using Blocks.Genesis;
using DomainService.Workflow.Events;
using DomainService.Workflow.Repositories;
using DomainService.Workflow.Enums;
using DomainService.Workflow.Models;
using DomainService.Workflow.Utils;
using Microsoft.Extensions.Logging;
using DomainService.Workflow.Nodes;
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

        public WorkflowEngineService(
            IWorkflowExecutionRepository workflowExecutionRepository,
            IEnumerable<INodeExecutor> nodeExecutors,
            IMessageClient messageClient,
            ILogger<WorkflowEngineService> logger
            )
        {
            _workflowExecutionRepository = workflowExecutionRepository;
            _nodeExecutors = nodeExecutors;
            _messageClient = messageClient;
            _logger = logger;
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
        private async Task ExecuteNodeAsync(AddExcuationNodeEvent dto, Func<List<AddExcuationNodeEvent>, Task> dispatchNextNodes)
        {
            var prepared = await PrepareNodeForExecutionAsync(dto);
            if (prepared == null) return;

            var (execution, node, nodeExecution, nodeExecutionContext, executor) = prepared.Value;

            try
            {
                var result = await executor.RunAsync(nodeExecutionContext);
                if (!result.IsSuccess)
                {
                    await FailNodeExecutionAsync(execution, nodeExecution, new Exception(result.ErrorMessage));
                }
                else
                {
                    var nextEvents = await CompleteNodeExecutionAsync(nodeExecutionContext, execution, node, nodeExecution, result);
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
        private async Task<List<AddExcuationNodeEvent>> CompleteNodeExecutionAsync(NodeExecutionContext context, WorkflowExecutionModel execution, NodeModel node, NodeExecutionModel nodeExecution, NodeExecutionResult result)
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

        public async Task<WorkflowExecutionModel?> ExecuteStepNodeAsync(string executionId, string targetNodeId, string? sourceExecutionId = null)
        {
            var execution = await _workflowExecutionRepository.GetByIdAsync(executionId, sourceExecutionId ?? "");
            var workflow = execution?.WorkflowSnapshot;
            // find tropological order of nodes to execute from targetNodeId to all downstream nodes



        }

        private List<NodeModel> GetAncestorNodesAsync(WorkflowModel workflow, string nodeId)
        {
            var ancestors = new List<NodeModel>();
            var incomingEdges = workflow.Edges.Where(e => e.Target == nodeId).ToList();
            foreach (var edge in incomingEdges)
            {
                var node = workflow.Nodes.FirstOrDefault(n => n.Id == edge.Source);
                if (node != null)
                {
                    ancestors.Add(node);
                    ancestors.AddRange(GetAncestorNodesAsync(workflow, node.Id));
                }
            }
            return ancestors;
        }
    }
}
