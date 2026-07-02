# Refactor `ExecuteStepNodeAsync` — cache cascade with strict node equality and PinData override

## Goal

Rewrite `WorkflowEngineService.ExecuteStepNodeAsync` (`server/DomainService/Workflow/Services/WorkflowEngineService.cs:425`) so that:

1. **`sourceExecutionId == null`** → every node in the topological ancestors+target sequence is executed (no cache).
2. **`sourceExecutionId != null`** → iterate the topological sequence; a source-cache hit is allowed only while **all** of the following hold for the current node:
   - source execution has a `NodeExecution` for that node,
   - source `RunIndex == i + 1` (order match),
   - source workflow's `NodeModel` equals the current workflow's `NodeModel` exactly (full JSON equality, Id included).
   The first failure flips a one-way `cacheEligible` flag to `false`; every remaining node is executed.
3. **PinData override** applies **only in the execute branch**. When the executor runs for a node that has `PinData`, the executor runs in full (side-effects, context updates) but its `OutputItems` are replaced with items synthesized from `node.PinData` before persistence. Cache hits are not overridden — the cached items already reflect the pinned values (the source ran the same pinned node).

## Affected files

- `server/DomainService/Workflow/Services/WorkflowEngineService.cs` — only file changed.

## Untouched

- `RunNodeAsync`, `RunNodeInProcessAsync`, queue-mode / in-process pinData semantics.
- `GetTopologicalAncestorsAndTarget`, `MaterializePinDataAsync`, `TryMaterializeFromSourceExecutionAsync`, `DeepCopyBson`.

## Tasks

1. **Add node-equality helper** next to `DeepCopyBson`:
   ```csharp
   private static bool NodesAreEquivalent(NodeModel source, NodeModel current)
   {
       return JsonConvert.SerializeObject(source) == JsonConvert.SerializeObject(current);
   }
   ```
   Compares the full serialized JSON (Id, Name, Category, Type, Version, Position.X/Y, Parameters, Settings, PinData). Strict by design — any field change invalidates cache.

2. **Add pinData-output builder** alongside `MaterializePinDataAsync`:
   ```csharp
   private static List<NodeOutputItem> BuildPinDataOutputItems(NodeModel node, NodeExecutionContext context)
   {
       var items = new List<NodeOutputItem>(node.PinData!.Count);
       int index = 0;
       foreach (var pinValue in node.PinData!)
       {
           items.Add(new NodeOutputItem
           {
               Branch = "main",
               ParentItemIds = new List<string>(),
               Data = new NodeOutputItemData
               {
                   Parameters = node.Parameters ?? new BsonDocument(),
                   Input = new BsonDocument(),
                   Output = pinValue
               }
           });
           index++;
       }
       return items;
   }
   ```
   Pure function; mirrors the item-construction block in `MaterializePinDataAsync`. Note: `index` is currently unused — drop it unless `NodeOutputItem` exposes ordering metadata to persist (it does not today).

3. **Extend `ExecuteNodeAsync`** with an optional result-transform hook (default `null`):
   ```csharp
   private async Task ExecuteNodeAsync(
       AddExcuationNodeEvent dto,
       Func<List<AddExcuationNodeEvent>, Task> dispatchNextNodes,
       Func<NodeExecutionContext, NodeModel, NodeExecutionResult, NodeExecutionResult>? postProcessResult = null)
   ```
   After `var result = await executor.RunAsync(nodeExecutionContext);`, if `postProcessResult != null`, apply it (`result = postProcessResult(nodeExecutionContext, node, result);`). Then proceed to the existing success/fail branches. Queue/in-process callers pass `null` (unchanged behaviour).

4. **Rewrite `ExecuteStepNodeAsync`** body:

   ```text
   // existing guards: execution != null, targetNode != null,
   // sourceExecution load + tenant/status checks
   // NEW guard: sourceExecution != null && sourceExecution.WorkflowId != execution.WorkflowId
   //            → sourceExecution = null   // cross-workflow cache disabled

   var ordered = GetTopologicalAncestorsAndTarget(workflow, targetNodeId).ToList();
   var cacheEligible = sourceExecution != null;          // one-way false trip
   var remap = new Dictionary<string, string>();
   var noopDispatch = new Func<List<AddExcuationNodeEvent>, Task>(_ => Task.CompletedTask);

   for (int i = 0; i < ordered.Count; i++)
   {
       var node = ordered[i];

       // refresh + early-exit on Failed (after each iteration)
       if (execution.Status == WorkflowExecutionStatus.Failed) return execution;

       // 1) Cache-eligible branch
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
               && await TryMaterializeFromSourceExecutionAsync(execution, sourceExecution, node, remap))
           {
               execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
               if (execution == null) return null;
               if (execution.Status == WorkflowExecutionStatus.Failed) return execution;
               continue;
           }

           // Any cache miss (node missing, order mismatch, definition drift, or remap fail)
           // flips the one-way flag.
           cacheEligible = false;
       }

       // 2) Execute branch (cacheEligible == false, OR sourceExecution == null from the start)

       Func<NodeExecutionContext, NodeModel, NodeExecutionResult, NodeExecutionResult>? hook = null;
       if (node.PinData != null && node.PinData.Count > 0)
       {
           hook = (_, n, _) => NodeExecutionResult.Successful(BuildPinDataOutputItems(n, _));
           // Note: closure captures `n`; assign `n` not `node` to avoid closure-on-loop-var
       }

       var evt = new AddExcuationNodeEvent
       {
           ProjectKey = execution.TenantId,
           WorkflowId = execution.WorkflowId,
           WorkflowExecutionId = execution.Id,
           NodeId = node.Id
       };

       await ExecuteNodeAsync(evt, noopDispatch, hook);

       execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
       if (execution == null) return null;
       if (execution.Status == WorkflowExecutionStatus.Failed) return execution;
   }

   await _workflowExecutionRepository.AtomicFinalizeExecutionAsync(execution.Id, execution.TenantId);
   return await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
   ```

   Implementation notes:
   - Use a local `n` inside the hook closure to avoid the C# loop-variable capture trap, e.g.:
     ```csharp
     var n = node;
     hook = (_, _, _) => NodeExecutionResult.Successful(BuildPinDataOutputItems(n, ctx));
     ```
   - `BuildPinDataOutputItems` takes `NodeExecutionContext` as its second parameter to keep the signature uniform with the hook, but it currently ignores it. That's fine — keep it for future use.

5. **Helpers remain untouched** — `MaterializePinDataAsync`, `TryMaterializeFromSourceExecutionAsync`, `DeepCopyBson`, `GetTopologicalAncestorsAndTarget`.

## Behaviour matrix

| sourceExecutionId | Cache signals valid | Node has PinData | Result |
|---|---|---|---|
| null | n/a | no | execute |
| null | n/a | yes | execute + PinData override |
| set | yes | no | cache hit |
| set | yes | yes | cache hit (cached items already reflect PinData) |
| set | no | no | execute |
| set | no | yes | execute + PinData override |

`cacheEligible` flips to `false` on the first non-cache iteration and stays `false` for the remainder (one-way cascade).

## Risks

- **Pinned nodes still run their executor's side-effects in step mode** (HTTP, DB, email). If undesired, add a follow-up `INodeExecutor.SupportsPinnedReadOnly` flag and short-circuit when `node.PinData != null`.
- **Strict JSON equality** means trivial edits (whitespace in `BsonDocument`, position drift, renamed node) invalidate cache. This is the user's stated intent ("all field include id. both node same").
- **Cross-workflow cache silently disabled** by the `WorkflowId` guard. Confirm this is the desired safety policy.
- **`TryMaterializeFromSourceExecutionAsync` does not replay `ContextUpdates`** — cache hits skip the executor entirely, including its `result.ContextUpdates`. Acceptable because context updates are keyed by node name and a re-run with the same node definition would produce the same updates, but call out in code review.
- **Closure capture in the PinData hook** — must alias `node` to a local before constructing the lambda.

## Validation

1. `dotnet build server/DomainService/DomainService.csproj` — 0 errors.
2. Manual scenarios (each one `POST /workflows/{id}/step`):
   1. `sourceExecutionId=null`, no PinData → every node executes.
   2. `sourceExecutionId=null`, target has PinData → executor runs, output items equal PinData count.
   3. `sourceExecutionId` valid, all node definitions identical, `RunIndex` sequence matches → every node cached (no executor invocations).
   4. `sourceExecutionId` valid, current workflow's node has any field change (Parameter tweak, rename, position drift) → that node executes; cascade no-cache downstream.
   5. `sourceExecutionId` valid, source's `RunIndex` differs from current iteration index → cache invalid; cascade.
   6. `sourceExecutionId` valid, source execution's `WorkflowId` differs from current → `sourceExecution` nulled; all nodes execute.
   7. `sourceExecutionId` valid, node has PinData + source has matching cache entry → cache hit (items equal PinData count, executor not invoked).
   8. `sourceExecutionId` valid, node has PinData + source has no matching entry / definition drift → execute + PinData override.

## Out of scope

- Queue-mode / in-process path (`RunNodeAsync`, `RunNodeInProcessAsync`) pinData semantics — unchanged.
- Schema / migration of existing `WorkflowExecutionModel` documents.
- Re-entry safety / concurrent-step safety of `ExecuteStepNodeAsync`.
- Suppression of executor side-effects when PinData is present in step mode (flagged as a possible follow-up).