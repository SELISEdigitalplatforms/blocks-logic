# Plan: Implement `ExecuteStepNodeAsync` in WorkflowEngineService

## Goal

Implement the empty `WorkflowEngineService.ExecuteStepNodeAsync` (`server/DomainService/Workflow/Services/WorkflowEngineService.cs:423`) so that, given a target node, it synchronously walks every ancestor, optionally short-circuiting with PinData or a source execution's cached items, then runs the target node itself, and returns the updated execution.

## Context (from code inspection)

- `IWorkflowEngineService.ExecuteStepNodeAsync(executionId, targetNodeId, sourceExecutionId?)` is already declared (`IWorkflowEngineService.cs:13`) and called from `WorkflowExecutionService.StepExecuteAsync` (`WorkflowExecutionService.cs:683`) with `execution.Id` as the freshly created test execution id and an optional `SourceExecutionId`.
- Existing helper `GetAncestorNodesAsync` (`WorkflowEngineService.cs:434`) does DFS over edges but is non-topological and not currently called.
- The existing executor pipeline is `ExecuteNodeAsync(dto, dispatchNextNodes)` → `PrepareNodeForExecutionAsync` → `executor.RunAsync` → `CompleteNodeExecutionAsync` / `FailNodeExecutionAsync` (`WorkflowEngineService.cs:55-79`, `86-152`, `297-421`).
- In-process synchronous dispatch is already available via `DispatchNodesImmediateAsync` (`WorkflowEngineService.cs:173`) and `RunNodeInProcessAsync` (`WorkflowEngineService.cs:46`).
- PinData lives on `NodeModel.PinData` (`NodeModel.cs:15`) as a `BsonArray`.
- The repository can fetch items for a set of `(NodeId, Branch)` pairs via `GetItemsByNodeIdsAsync` (`Repositories/WorkflowExecutionRepository.cs:186`).

## Design (resolved with user)

1. **Synchronous, fail-fast**. Walk ancestors + target in topological order. On any failure, stop and return the execution.
2. **PinData short-circuit**. If the current node has `PinData`, do not call the executor; produce a synthetic completed `NodeExecution` and one `WorkflowItemExecutionModel` per pin entry; persist items + status atomically using the same primitives as `CompleteNodeExecutionAsync`; then advance `ActiveNodeIds` exactly as the normal completion path does.
3. **Source-execution short-circuit**. If no PinData and `sourceExecutionId` is provided AND the source execution contains items for that node, copy them as new items in the new execution (fresh `Id`, fresh `NodeExecutionId` pointing at a new synthetic `NodeExecutionModel`), mark the node completed, advance `ActiveNodeIds`.
4. **Otherwise**: run the existing in-process pipeline (`RunNodeInProcessAsync` style) by invoking the internal `ExecuteNodeAsync(dto, DispatchNodesImmediateAsync)`.
5. **Target node**: included in the walk after all ancestors (same rule applies — PinData or source cache wins).

## Tasks (ordered)

1. **Topological ordering helper** — add a private method `IEnumerable<NodeModel> GetTopologicalAncestorsAndTarget(WorkflowModel workflow, string targetNodeId)` in `WorkflowEngineService.cs`:
   - Compute the set of `targetNodeId` plus all transitive ancestors reachable by following edges backwards from the target (reuse the existing `Edges.Where(e => e.Target == …)` traversal, with a `visited` set to avoid cycles).
   - Return nodes ordered such that every node appears after all of its ancestors (Kahn's algorithm or a DFS with post-order append). Trigger nodes (no incoming edges) come first.

2. **PinData materialization helper** — add a private method `Task MaterializePinDataAsync(WorkflowExecutionModel execution, NodeModel node)`:
   - Build a `NodeExecutionModel` with `Status = Completed`, `StartedAt = EndedAt = UtcNow`, `OutputItemCount = pinData.Count`, `RunIndex = execution.NodeExecutions.Count + 1`.
   - For each `BsonValue` in `node.PinData`, create a `WorkflowItemExecutionModel` with a new `Id`, the synthetic `NodeExecutionId`, `Branch = "main"`, `Data = new BsonDocument("Output", element)`, `ItemIndex = index++`, empty `ParentItemIds`, and an `AncestorMap` built from the node's direct parents' last items (mirroring `CompleteNodeExecutionAsync:307-314`).
   - Persist items with `AddItemsAsync(execution.TenantId, outputItems)`, then `AtomicAddNodeExecutionAsync` for the `NodeExecution` (it must be added before it's referenced by items), then `AtomicUpdateNodeExecutionCompletedAsync`, then `AtomicCompleteNodeAsync` advancing `ActiveNodeIds` to the node's downstream targets.
   - Update the in-memory `execution.NodeExecutions` and `execution.ActiveNodeIds` so subsequent steps see consistent state.

3. **Source-execution materialization helper** — add `Task<bool> TryMaterializeFromSourceAsync(WorkflowExecutionModel execution, NodeModel node, string sourceExecutionId)`:
   - Fetch items for `(node.Id, "main")` from the source execution via `_workflowExecutionRepository.GetItemsByNodeIdsAsync(sourceExecutionId, [{NodeId, Branch}], execution.TenantId)`.
   - If the list is empty, return `false`.
   - Otherwise clone each item (new `Id`, new `NodeExecutionId` pointing at a fresh `NodeExecutionModel`, `WorkflowExecutionId = execution.Id`, same `Data`, same `ParentItemIds`, `AncestorMap` remapped to the new execution's item ids where possible; if remap is not feasible, keep the map as-is and log a warning). Persist via the same `AddItemsAsync` + `AtomicAddNodeExecutionAsync` + `AtomicUpdateNodeExecutionCompletedAsync` + `AtomicCompleteNodeAsync` sequence as PinData.
   - Return `true`.

4. **Implement `ExecuteStepNodeAsync`** — replace the empty body at `WorkflowEngineService.cs:423`:
   ```
   1. execution = _workflowExecutionRepository.GetByIdAsync(executionId, sourceExecutionId ?? "")
      (use execution.TenantId for subsequent repo calls; pass "" to GetByIdAsync for the new execution only)
   2. If execution == null or WorkflowSnapshot == null → return null.
   3. orderedNodes = GetTopologicalAncestorsAndTarget(execution.WorkflowSnapshot, targetNodeId).
   4. For each node in orderedNodes:
        a. If node.PinData != null && node.PinData.Count > 0:
             await MaterializePinDataAsync(execution, node); continue.
        b. If sourceExecutionId != null:
             if (await TryMaterializeFromSourceAsync(execution, node, sourceExecutionId)) continue;
        c. Otherwise build AddExcuationNodeEvent { WorkflowExecutionId = execution.Id, WorkflowId = execution.WorkflowId, NodeId = node.Id, ProjectKey = execution.TenantId }
           and await ExecuteNodeAsync(evt, DispatchNodesImmediateAsync).
        d. If execution.Status == Failed after step c → return execution (fail-fast).
   5. Return await _workflowExecutionRepository.GetByIdAsync(execution.Id, "" /* tenant not needed for refresh; verify repo signature */).
   ```
   Note: `GetByIdAsync` currently takes `(string id, string tenantId)`. Reuse `execution.TenantId` as the tenant arg for the refresh.

5. **In-memory execution consistency** — the existing `ExecuteNodeAsync(dto, dispatchNextNodes)` re-reads execution from the repo, so the in-memory `execution` reference may go stale after each call. Re-fetch `execution` after every step in the loop with `_workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId)` (no refactor of existing helpers — adds only new code in this file).

6. **Tests** — add unit/integration tests covering:
   - All ancestors + target execute when no PinData and no source.
   - A node with PinData short-circuits; its output items appear under the new execution and downstream nodes receive them as inputs.
   - With `sourceExecutionId`, cached items are copied (fresh ids) and ancestors are not re-run.
   - Mix: some ancestors use PinData, one uses source cache, one executes normally.
   - Failure of any non-short-circuited node stops the loop and returns a failed execution.
   - Cycles in the graph do not infinite-loop (visited set).

## Affected files

- `server/DomainService/Workflow/Services/WorkflowEngineService.cs` — implement method + add helpers.
- `server/DomainService/Workflow/Services/IWorkflowEngineService.cs` — no signature change.
- `server/DomainService/Workflow/Services/WorkflowExecutionService.cs` — no change; `StepExecuteAsync` already calls the method.
- `server/Api/Controllers/WorkflowController.cs` — no change.
- New test file under `server/Tests/` (or equivalent) covering the cases above.

## Constraints

- **Do not change other implementations.** Only add new code inside `WorkflowEngineService.cs` (and tests). Do not modify the existing `ExecuteNodeAsync` / `PrepareNodeForExecutionAsync` / `CompleteNodeExecutionAsync` / `FailNodeExecutionAsync` / `RunNodeInProcessAsync` / `DispatchNodesImmediateAsync` paths, repository interfaces, models, or other services. If the existing executor pipeline re-reads `execution` from the repo, re-fetch the in-memory model after each step (preferred) rather than refactoring shared helpers.

## Risks / open items

- `GetByIdAsync(id, tenant)` currently throws on miss; the new method passes `sourceExecutionId ?? ""` as tenant for the initial fetch, which is a pre-existing quirk in the stub (`WorkflowEngineService.cs:425`). We will mirror the existing behaviour for the initial load, then switch to `execution.TenantId` for everything that follows once `execution` is loaded. Confirm with reviewer that this matches intent.
- `AncestorMap` remap when cloning items from a source execution is not always meaningful (source item ids differ from new item ids). Plan keeps the map as-is with a warning; if a later task requires correct remapping, add a follow-up.
- Fail-fast on ancestor failure means a partially-populated execution is returned. Callers (`StepExecuteAsync` → controller) currently ignore the returned execution; if they want to surface partial state, that's a separate UI/UX task.