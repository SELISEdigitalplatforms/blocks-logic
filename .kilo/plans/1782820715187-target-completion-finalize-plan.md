# Plan: Finalize execution status after `ExecuteStepNodeAsync` target runs

## Goal

After `ExecuteStepNodeAsync` finishes executing the target node (including all freshly-run and source-cache-materialized ancestors), clear `ActiveNodeIds` and mark the execution as `Status = Completed` with `FinishedAt = now`. Step-mode semantics: "stop here, no dangling active nodes, no Running status."

## Resolved decisions

1. **Always finalize after loop.** After the `for` loop exits normally, call a new atomic repo primitive that sets `ActiveNodeIds = []`, `Status = Completed`, `FinishedAt = now`. Idempotent.
2. **Preserve Failed status.** All early returns in `ExecuteStepNodeAsync` (execution null, target not found, `execution.Status == Failed`) precede the finalize call, so a failed execution is never finalized.
3. **Atomic repo primitive.** Add `AtomicFinalizeExecutionAsync(tenantId, executionId)` to `IWorkflowExecutionRepository` and implement in `WorkflowExecutionRepository.cs`. Matches existing pattern (`AtomicAddNodeExecutionAsync`, `AtomicCompleteNodeAsync`, `AtomicUpdateNodeExecution*`) over `UpdateAsync`/`ReplaceOneAsync`.
4. **No model, DTO, interface, or controller changes.**
5. **No new tests this round.**

## Why a new repo method (not relying on existing `AtomicCompleteNodeAsync`)

`AtomicCompleteNodeAsync` only flips `Status` to `Completed` when `ActiveNodeIds` becomes empty. In step mode, after the target node executes:

- `CompleteNodeExecutionAsync` builds `nextNodeIds` from edges where `Source == targetNodeId` and calls `AtomicCompleteNodeAsync`, which `$addToSet`s them into `ActiveNodeIds`.
- `noopDispatch` prevents them from running.
- `ActiveNodeIds` stays non-empty → `Status` remains `Running` with phantom downstream IDs.

For non-leaf targets the execution would be permanently stuck `Running` with stale `ActiveNodeIds`. An explicit finalize after the loop guarantees the desired end state regardless of target position in the DAG.

## Implementation

### Affected files

- `server/DomainService/Workflow/Repositories/IWorkflowExecutionRepository.cs` — add interface method.
- `server/DomainService/Workflow/Repositories/WorkflowExecutionRepository.cs` — implement method (mirroring `AtomicCompleteNodeAsync` style).
- `server/DomainService/Workflow/Services/WorkflowEngineService.cs` — call finalize after the loop in `ExecuteStepNodeAsync`, refresh, return.

### New repo method

```csharp
public async Task AtomicFinalizeExecutionAsync(string executionId, string tenantId)
{
    var collection = GetCollection(tenantId);
    var filter = Builders<WorkflowExecutionModel>.Filter.Eq(e => e.Id, executionId);
    var update = Builders<WorkflowExecutionModel>.Update
        .Set(e => e.ActiveNodeIds, new List<string>())
        .Set(e => e.Status, WorkflowExecutionStatus.Completed)
        .Set(e => e.FinishedAt, DateTime.UtcNow);
    await collection.UpdateOneAsync(filter, update);
}
```

`List<string>` is the type of `WorkflowExecutionModel.ActiveNodeIds`. Single MongoDB update, no need for transactional safeguards (concurrency is bounded by the same `executionId`).

### `ExecuteStepNodeAsync` change

Replace the current final return:

```csharp
return await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
```

with:

```csharp
await _workflowExecutionRepository.AtomicFinalizeExecutionAsync(execution.Id, execution.TenantId);
return await _workflowExecutionRepository.GetByIdAsync(execution.Id, execution.TenantId);
```

No other changes to `ExecuteStepNodeAsync`. Source-cache branch, PinData branch, and early returns are untouched.

## Tasks (ordered)

1. Add `Task AtomicFinalizeExecutionAsync(string executionId, string tenantId)` to `IWorkflowExecutionRepository`.
2. Implement it in `WorkflowExecutionRepository` with the body above.
3. Call it from `WorkflowEngineService.ExecuteStepNodeAsync` after the `for` loop and before the final `GetByIdAsync`.
4. Rebuild `server/DomainService/DomainService.csproj` and confirm zero errors.

## Constraints (hard)

- No model, DTO, interface signature (other than the new repo method), or controller changes.
- Do not modify `ExecuteNodeAsync`, `PrepareNodeForExecutionAsync`, `CompleteNodeExecutionAsync`, `FailNodeExecutionAsync`, `RunNodeInProcessAsync`, `DispatchNodesImmediateAsync`, `MaterializePinDataAsync`, `GetTopologicalAncestorsAndTarget`, or `TryMaterializeFromSourceExecutionAsync`.
- No tests this round.
- Atomic primitive must mirror existing patterns; do not use `UpdateAsync`/`ReplaceOneAsync` for this transition.

## Risks / open items

- **Idempotency.** If the target is a leaf and `AtomicCompleteNodeAsync` already auto-set `Status=Completed` and emptied `ActiveNodeIds`, the explicit finalize re-writes the same fields. Safe.
- **PinData-only paths.** Same early-return shape, same finalize after loop. No behavioral change beyond the end-state being consistently `Completed`.
- **Source-cache non-leaf targets.** Cached ancestors materialize Completed `NodeExecution`s and still call `AtomicCompleteNodeAsync` to update `ActiveNodeIds`; target runs fresh; loop exits; finalize clears whatever `ActiveNodeIds` remained.
- **Cross-tenant leakage.** `AtomicFinalizeExecutionAsync` uses `execution.TenantId`, which was already validated upstream; `GetCollection` is tenant-scoped. No new surface area.

## Validation

- Build `DomainService.csproj` and confirm 0 errors.
- Trace four scenarios end-to-end:
  1. Leaf target, all fresh → finalize is a no-op re-write (idempotent).
  2. Leaf target with cached ancestors → same as (1).
  3. Non-leaf target, all fresh → `AtomicCompleteNodeAsync` left downstream IDs in `ActiveNodeIds`; finalize clears them and sets `Status=Completed`.
  4. Non-leaf target with cached ancestors → same end state via finalize.
- In all cases, post-loop `GetByIdAsync` must return an execution with `Status = Completed`, `ActiveNodeIds = []`, `FinishedAt != null`.
- Failed execution path: trigger failure on any node → loop early-returns before finalize → `Status` stays `Failed`, `FinishedAt` set by `AtomicUpdateNodeExecutionFailedAsync`.
