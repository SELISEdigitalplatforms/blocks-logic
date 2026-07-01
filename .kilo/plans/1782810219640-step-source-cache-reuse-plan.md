# Plan: Source-execution cache reuse in `ExecuteStepNodeAsync`

## Goal

Extend step execution so that, when `StepExecuteRequestDto.SourceExecutionId` is provided, each ancestor of the target is reused from the source execution's completed `NodeExecution` instead of being re-executed. The target node always runs fresh. Copied `WorkflowItemExecutionModel` records receive **new** `Id`s, and their `ParentItemIds` + `AncestorMap` are remapped from source item IDs to the new item IDs.

## Resolved decisions

1. **Cache scope — ancestors only.** Target always executes fresh.
2. **Source validation** — source must exist, belong to the same tenant, and `WorkflowExecution.Status == Completed`. The specific `NodeExecution` for an ancestor must have `Status == Completed`. Otherwise that ancestor falls back to fresh execution.
3. **No context propagation.** Per user: do not add `ContextUpdates` anywhere; do not modify `NodeExecutionModel` or other methods to replay context.
4. **PinData precedence** — `PinData` short-circuits source cache when both apply to a node (existing behavior wins).
5. **ID remap** — per-step in-memory `Dictionary<string,string> sourceItemId → newItemId`, populated only by cached-ancestor materializations. Freshly executed ancestors do NOT add entries.
6. **Conservative cascade** — when processing a candidate cached ancestor, before materializing, verify that every `ParentItemIds` and every `AncestorMap` value on the candidate's source items exists in the remap dictionary. If any reference is unmapped (because an upstream ancestor fell back to fresh execution), this candidate and all subsequent ancestors fall back to fresh execution too. No heuristic item matching across executions.

## Design

### Flow in `ExecuteStepNodeAsync`

For each node `N` in the topo-ordered ancestor+target list:

1. If `N == targetNodeId` → always run fresh (`ExecuteNodeAsync(evt, noopDispatch)`). Skip cache check.
2. If `N.PinData` non-empty → `MaterializePinDataAsync(execution, N)` (existing behavior). Skip cache check. **Do not** add entries to remap dictionary (PinData items are not from source).
3. If `sourceExecution != null` and a Completed `NodeExecution` for `N.NodeId` exists in source → `MaterializeFromSourceExecutionAsync(execution, sourceExecution, sourceNodeExec, N, remap)`. This helper:
   - Pre-flight: fetch source items via `_workflowExecutionRepository.GetAllItemsByNodeExecutionIdAsync(sourceNodeExec.Id, tenantId)`. For each source item, verify every id in `ParentItemIds` and every value in `AncestorMap` is present in `remap`. If any miss → log and fall through to fresh execution for `N` (and the conservative cascade for remaining ancestors).
   - Build new `NodeExecutionModel` with `Status = Completed`, fresh `Id`, mirrored `NodeId/NodeName/NodeType/NodeVersion`, fresh `RunIndex = execution.NodeExecutions.Count + 1`, fresh `StartedAt`/`EndedAt` = now, mirrored `OutputItemCount` / `OutputCountsByBranch`, mirrored `AttemptNumber`. No `Error`.
   - For each source item:
     - Generate fresh `Id`.
     - Add `sourceItem.Id → newItem.Id` to `remap`.
     - Set `WorkflowExecutionId = execution.Id`, `TenantId = execution.TenantId`, `NodeId = N.Id`, `NodeExecutionId = newNodeExecution.Id`, `NodeName = N.Name`, `Branch = sourceItem.Branch`, `ItemIndex = sourceItem.ItemIndex`.
     - `ParentItemIds` = sourceItem.ParentItemIds.Select(oldId => remap[oldId]).ToList().
     - `AncestorMap` = sourceItem.AncestorMap.ToDictionary(kv => kv.Key, kv => remap[kv.Value]).
     - `Data` = deep copy of `sourceItem.Data` (`NodeOutputItemData` with `Parameters`, `Input`, `Output` `BsonDocument`s cloned via `BsonDocument.Parse(source.ToJson())` pattern or element-wise copy — pick whichever the rest of the codebase uses; deep copy is required so downstream mutations don't bleed into source data).
   - Persist items via `_workflowExecutionRepository.AddItemsAsync(execution.TenantId, outputItems)`.
   - Append `newNodeExecution` to `execution.NodeExecutions` and persist via `_workflowExecutionRepository.AtomicAddNodeExecutionAsync(execution.Id, execution.TenantId, newNodeExecution)`. The repo's existing implementation sets execution `Status = Running`; that is acceptable.
   - Update the synthetic `NodeExecution` in-memory to `Status = Completed` then call `_workflowExecutionRepository.AtomicUpdateNodeExecutionCompletedAsync(execution.Id, execution.TenantId, newNodeExecution.Id, outputItems.Count, newNodeExecution.OutputCountsByBranch, contextUpdates: null)`.
   - Compute `nextNodeIds` from `execution.WorkflowSnapshot.Edges.Where(e => e.Source == N.Id).Select(e => e.Target).Distinct()` and call `_workflowExecutionRepository.AtomicCompleteNodeAsync(execution.Id, execution.TenantId, N.Id, nextNodeIds)` so `ActiveNodeIds` reflects state correctly.
   - Refresh `execution = await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId)` and `continue`.
4. Otherwise → execute fresh: build `AddExcuationNodeEvent`, call `ExecuteNodeAsync(evt, noopDispatch)`, refresh `execution`. Remap dictionary is NOT updated for fresh nodes.

### Source execution loading

Inside `ExecuteStepNodeAsync`, after initial execution load and target validation:

```
sourceExecution = null;
if (!string.IsNullOrEmpty(sourceExecutionId)) {
    sourceExecution = await _workflowExecutionRepository.GetByIdAsync(sourceExecutionId, tenantId);
    if (sourceExecution != null && sourceExecution.TenantId != execution.TenantId) sourceExecution = null;
    if (sourceExecution != null && sourceExecution.Status != WorkflowExecutionStatus.Completed) sourceExecution = null;
}
```

If `sourceExecution` ends up null, treat as no cache available and proceed with current behavior (PinData short-circuit + fresh execution).

### Interface / signature changes

- `IWorkflowEngineService.ExecuteStepNodeAsync` — add optional `string? sourceExecutionId = null` parameter. Implementations and the single caller updated accordingly.
- `StepExecuteRequestDto` — add `public string? SourceExecutionId { get; set; }`.
- `WorkflowExecutionService.StepExecuteAsync` — pass `dto.SourceExecutionId` through to `ExecuteStepNodeAsync`.

## Affected files

- `server/DomainService/Workflow/Dtos/StepExecuteRequestDto.cs` — add `SourceExecutionId`.
- `server/DomainService/Workflow/Services/IWorkflowEngineService.cs` — extend `ExecuteStepNodeAsync` signature with optional `sourceExecutionId`.
- `server/DomainService/Workflow/Services/WorkflowExecutionService.cs:683` — pass `dto.SourceExecutionId` to `ExecuteStepNodeAsync`.
- `server/DomainService/Workflow/Services/WorkflowEngineService.cs` — load source execution in `ExecuteStepNodeAsync`, declare remap dictionary, call new helper, add `MaterializeFromSourceExecutionAsync` private method. Do **not** modify `ExecuteNodeAsync`, `PrepareNodeForExecutionAsync`, `CompleteNodeExecutionAsync`, `FailNodeExecutionAsync`, `RunNodeInProcessAsync`, `DispatchNodesImmediateAsync`, `MaterializePinDataAsync`, `GetTopologicalAncestorsAndTarget`.

No repository, model, or controller changes.

## Tasks (ordered)

1. Add `SourceExecutionId` to `StepExecuteRequestDto`.
2. Extend `IWorkflowEngineService.ExecuteStepNodeAsync` with `string? sourceExecutionId = null`.
3. Update `WorkflowExecutionService.StepExecuteAsync` call site.
4. In `ExecuteStepNodeAsync`:
   - Load source execution; apply tenant + Completed validation; null if any check fails.
   - Declare `var remap = new Dictionary<string, string>();` before the loop.
   - In the per-node loop, after the PinData branch and before the fresh-execution branch, add the cache-eligibility check + new helper invocation, gated on `N.Id != targetNodeId && sourceExecution != null`.
5. Implement `MaterializeFromSourceExecutionAsync`:
   - Find the source `NodeExecution` for `N.NodeId` with `Status == Completed`; pick the highest-`RunIndex` if multiple.
   - Fetch source items via `_workflowExecutionRepository.GetAllItemsByNodeExecutionIdAsync`.
   - Pre-flight remap check on every `ParentItemIds` and `AncestorMap` value across all source items; on miss, return without changes (caller falls back to fresh).
   - Build new `NodeExecutionModel` (Completed metadata).
   - Build new `WorkflowItemExecutionModel` list with new IDs and remapped references; populate `remap` as items are built.
   - Persist items + node execution + completion + active-node transition via existing repo primitives.
6. Return `execution` (refreshed).

## Constraints (hard)

- **Do not modify `ExecuteNodeAsync`, `PrepareNodeForExecutionAsync`, `CompleteNodeExecutionAsync`, `FailNodeExecutionAsync`, `RunNodeInProcessAsync`, `DispatchNodesImmediateAsync`, `MaterializePinDataAsync`, `GetTopologicalAncestorsAndTarget`.**
- **Do not add `ContextUpdates` to `NodeExecutionModel`** or introduce any per-node context snapshotting.
- **No test files this round.**
- **No controller changes** beyond what's already there.
- Refetch `execution` after every step (no shared in-memory mutation across iterations).

## Risks / open items

- Deep-copy semantics for `BsonDocument` fields on `NodeOutputItemData`: choose `BsonDocument.Parse(source.ToJson())` style (matches existing codebase patterns) to avoid mutating source data through shared references.
- Conservative cascade means mixed scenarios (e.g., source has cached items for some but not all ancestors) yield partial cache reuse — the prefix up to the first miss is cached, the rest is fresh. This is the intended behavior for v1.
- Source execution from a different tenant is silently ignored (cache skipped) rather than rejected. If a stricter posture is wanted later, raise an error instead.
- `OutputCountsByBranch` on the cached `NodeExecution` mirrors the source — semantically the items in the new execution should produce the same counts, so this is safe.