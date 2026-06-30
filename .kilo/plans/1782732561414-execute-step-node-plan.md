# Plan: Implement `ExecuteStepNodeAsync` in WorkflowEngineService

## Goal

Implement `WorkflowEngineService.ExecuteStepNodeAsync` (`server/DomainService/Workflow/Services/WorkflowEngineService.cs:423`) so that, given a target node, it synchronously walks every ancestor and the target itself in topological order: a node with `PinData` short-circuits to a synthetic completed node + items, otherwise the node runs through the existing in-process executor pipeline. Returns the updated execution.

## Context (from code inspection)

- `IWorkflowEngineService.ExecuteStepNodeAsync(executionId, targetNodeId, sourceExecutionId?)` is declared at `IWorkflowEngineService.cs:13`. The `sourceExecutionId` parameter is no longer wanted (it causes data loss) and must be removed.
- The only caller is `WorkflowExecutionService.StepExecuteAsync` at `WorkflowExecutionService.cs:683`, which passes `dto.SourceExecutionId` — that call site must be updated when the parameter is dropped.
- Existing executor pipeline: `ExecuteNodeAsync(dto, dispatchNextNodes)` → `PrepareNodeForExecutionAsync` → `executor.RunAsync` → `CompleteNodeExecutionAsync` / `FailNodeExecutionAsync` (`WorkflowEngineService.cs:55-79`, `86-152`, `297-421`).
- PinData lives on `NodeModel.PinData` (`NodeModel.cs:15`) as a `BsonArray`.
- Repository primitives used: `AtomicAddNodeExecutionAsync`, `AddItemsAsync`, `AtomicUpdateNodeExecutionCompletedAsync`, `AtomicCompleteNodeAsync`, `GetItemsByNodeIdsAsync`, `GetByIdAsync` (`Repositories/WorkflowExecutionRepository.cs`).

## Design (resolved with user)

1. **Synchronous, fail-fast**. Walk ancestors + target in topological order. On any executor failure, stop and return the execution.
2. **PinData short-circuit**. If a node has `PinData`, do not call the executor; produce a synthetic completed `NodeExecution` and one `WorkflowItemExecutionModel` per pin entry; persist items + status atomically using the same primitives as `CompleteNodeExecutionAsync`; then advance `ActiveNodeIds` exactly as the normal completion path does.
3. **Otherwise run normally**. Use the existing in-process pipeline by calling `ExecuteNodeAsync(evt, noopDispatch)` with a no-op dispatcher so downstream-of-target is not triggered.
4. **Drop `sourceExecutionId`** from the interface; no source-cache logic.

## Tasks (ordered)

1. **Update interface signature** in `server/DomainService/Workflow/Services/IWorkflowEngineService.cs:13`:
   - Change `ExecuteStepNodeAsync(string executionId, string targetNodeId, string? sourceExecutionId = null)` to `ExecuteStepNodeAsync(string executionId, string targetNodeId)`.

2. **Update caller** in `server/DomainService/Workflow/Services/WorkflowExecutionService.cs:683`:
   - Change `_workflowEngineService.ExecuteStepNodeAsync(execution.Id, dto.NodeId, dto.SourceExecutionId)` to `_workflowEngineService.ExecuteStepNodeAsync(execution.Id, dto.NodeId)`.
   - `StepExecuteRequestDto.SourceExecutionId` can remain in the DTO for backward compatibility with clients that still send it; it is simply ignored.

3. **Rewrite `ExecuteStepNodeAsync` body** in `server/DomainService/Workflow/Services/WorkflowEngineService.cs`:
   - Initial load: `execution = _workflowExecutionRepository.GetByIdAsync(executionId, execution.TenantId)`. (The empty-string tenant quirk from the original stub is gone — once execution is loaded we have its `TenantId`. On the first read, we don't know the tenant yet; pass `""` to mirror the existing call signature, and on miss throw via repo.)
   - Return early if `execution == null` or `execution.WorkflowSnapshot == null`.
   - Validate `targetNodeId` exists in `execution.WorkflowSnapshot.Nodes`; return `execution` unchanged if missing.
   - Compute `ordered = GetTopologicalAncestorsAndTarget(execution.WorkflowSnapshot, targetNodeId).ToList()`.
   - Define `var noopDispatch = new Func<List<AddExcuationNodeEvent>, Task>(_ => Task.CompletedTask);`
   - For each node:
     - If `node.PinData != null && node.PinData.Count > 0`: `await MaterializePinDataAsync(execution, node);` then `execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);` continue.
     - Else: build `AddExcuationNodeEvent { ProjectKey = execution.TenantId, WorkflowId = execution.WorkflowId, WorkflowExecutionId = execution.Id, NodeId = node.Id }` and `await ExecuteNodeAsync(evt, noopDispatch);`.
     - `execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);` if null, return null.
     - If `execution.Status == WorkflowExecutionStatus.Failed`, return `execution` (fail-fast).
   - Return `execution`.

4. **Add `GetTopologicalAncestorsAndTarget` private helper** in `WorkflowEngineService.cs`:
   - Build the set of `targetNodeId` plus all transitive ancestors by walking edges backwards, with a `visited` set for cycle safety.
   - Topo-sort via Kahn's algorithm over the restricted set (in-degree from edges where both endpoints are in the set), so each node appears after its parents.
   - Append any leftover nodes from the set that weren't reached by Kahn (cycle / disconnected fallback) to guarantee a total order without infinite loops.

5. **Add `MaterializePinDataAsync` private helper** in `WorkflowEngineService.cs`:
   - Build a `NodeExecutionModel` with `Status = Running`, `StartedAt = UtcNow`, `RunIndex = execution.NodeExecutions.Count + 1`; add to `execution.NodeExecutions`; persist via `AtomicAddNodeExecutionAsync`.
   - Compute parent ancestor map: fetch direct parents' items via `GetItemsByNodeIdsAsync(execution.Id, [(parentId, "main")], execution.TenantId)` and merge their `AncestorMap` entries plus a `parentName -> parentItem.Id` entry.
   - For each `BsonValue` in `node.PinData`, build a `WorkflowItemExecutionModel` with a fresh `Id`, the synthetic `NodeExecutionId`, `Branch = "main"`, `ParentItemIds = []`, `AncestorMap = { ...parentMap, [node.Name] = id }`, `Data = new NodeOutputItemData { Parameters = node.Parameters, Input = new BsonDocument(), Output = pinValue }`, `ItemIndex = index++`.
   - `await AddItemsAsync(execution.TenantId, outputItems)`.
   - Update in-memory `nodeExecution` to `Status = Completed`, set `OutputItemCount`, `OutputCountsByBranch`, `EndedAt`.
   - `await AtomicUpdateNodeExecutionCompletedAsync(...)` (pass `null` for context updates).
   - Compute `nextNodeIds` from `execution.WorkflowSnapshot.Edges.Where(e => e.Source == node.Id).Select(e => e.Target).Distinct()`.
   - `await AtomicCompleteNodeAsync(execution.Id, execution.TenantId, node.Id, nextNodeIds)`.

6. **Delete the now-unused `TryMaterializeFromSourceAsync` helper** and its call site (added in the previous iteration of this plan) from `WorkflowEngineService.cs`. Remove the related test if present (no tests are required by the user this round).

## Affected files

- `server/DomainService/Workflow/Services/IWorkflowEngineService.cs` — drop the `sourceExecutionId` parameter.
- `server/DomainService/Workflow/Services/WorkflowExecutionService.cs` — adjust the call site (drop the third argument).
- `server/DomainService/Workflow/Services/WorkflowEngineService.cs` — rewrite `ExecuteStepNodeAsync` body; add `GetTopologicalAncestorsAndTarget` and `MaterializePinDataAsync` helpers; remove `TryMaterializeFromSourceAsync` and any prior leftover.
- No repository or model changes.

## Constraints (user-stated, hard)

- **Do not change other methods.** No edits to existing `ExecuteNodeAsync` / `PrepareNodeForExecutionAsync` / `CompleteNodeExecutionAsync` / `FailNodeExecutionAsync` / `RunNodeInProcessAsync` / `DispatchNodesImmediateAsync` paths, repository interfaces, models, or any other service.
- **No test cases in this round.** Do not create or update any test files. `XUnitTest/Workflow/` directory does not need to be created.
- Re-fetch the in-memory `execution` after every step (no refactor of shared helpers).
- The `WorkflowExecutionService.StepExecuteAsync` call site update is the only call-site change required; controllers and the DTO are untouched.

## Risks / open items

- `GetByIdAsync(id, tenant)` requires a tenant on the first lookup before `execution.TenantId` is known; pass `""` (same as the existing stub) and rely on the repository's behavior — confirm reviewer agrees.
- Fail-fast on ancestor failure leaves a partially-populated execution; callers currently ignore the return value. Surfacing partial state is a separate UI/UX task and out of scope.
- Cycles in the graph are tolerated by `GetTopologicalAncestorsAndTarget` via the visited set and the Kahn fallback; no infinite loops.