# Plan: Finalize execution when `ExecuteNodeAsync` DTO has `destinationNodeId`

## Goal

In `WorkflowEngineService.ExecuteNodeAsync`, after a node executes successfully and `dto.destinationNodeId` is set, **skip dispatching downstream nodes** and **mark the workflow execution as Completed** (instead of letting it continue running).

## Background (verified in code)

- `AddExcuationNodeEvent` already declares `public string? destinationNodeId { get; set; }` (`server/DomainService/Workflow/Events/WorkflowExcuationEngineEvents.cs:11`) — currently unused.
- `ExecuteNodeAsync` (`WorkflowEngineService.cs:57-89`) is the single core pipeline used by:
  - `RunNodeAsync` (queue mode, line 40)
  - `RunNodeInProcessAsync` (in-process mode, line 48)
  - `ExecuteStepNodeAsync` (step mode, line 501 — already finalizes at line 508, so this feature mainly benefits queue and in-process paths)
- On success path: `nextEvents = await CompleteNodeExecutionAsync(...)` then `await dispatchNextNodes(nextEvents);` (lines 81-82).
- `_workflowExecutionRepository.AtomicFinalizeExecutionAsync(executionId, tenantId)` already exists (`WorkflowExecutionRepository.cs:99-109`) — atomically clears `ActiveNodeIds`, sets `Status = Completed`, `FinishedAt = UtcNow`. **Idempotent.**

## Scope decision (confirmed)

**Success-only finalize.** Failure path uses existing `FailNodeExecutionAsync` (already sets `Status = Failed`, `FinishedAt = UtcNow`).

## Tasks

1. **`server/DomainService/Workflow/Services/WorkflowEngineService.cs`** — modify `ExecuteNodeAsync` (lines 57-89):
   - After `await CompleteNodeExecutionAsync(...)` returns successfully (line 81), check `if (!string.IsNullOrEmpty(dto.destinationNodeId))`.
   - If true:
     - **Skip** `await dispatchNextNodes(nextEvents);` (do not enqueue downstream node events).
     - **Call** `await _workflowExecutionRepository.AtomicFinalizeExecutionAsync(execution.Id, execution.TenantId);`
     - Log a single info line: `"Execution {ExecutionId} finalized at destinationNodeId {DestinationNodeId}."`
   - Do not modify the `catch (Exception ex)` branch — existing failure handling stays untouched.

2. **`server/DomainService/Workflow/Dtos/StepExecuteRequestDto.cs`** — add property:
   ```csharp
   public string? DestinationNodeId { get; set; }
   ```
   (PascalCase to match the other DTO fields; mapping layer lower-cases to event field name.)

3. **`server/DomainService/Workflow/Services/WorkflowEngineService.cs` `ExecuteStepNodeAsync`** (line 433) — when building the local `AddExcuationNodeEvent` (line 493-499), propagate `destinationNodeId` from the call site. Add a new optional parameter to `ExecuteStepNodeAsync`:
   ```csharp
   Task<WorkflowExecutionModel?> ExecuteStepNodeAsync(
       string tenantId, string executionId, string triggerNodeId,
       string targetNodeId, string? destinationNodeId = null,
       string? sourceExecutionId = null);
   ```
   And set `evt.destinationNodeId = destinationNodeId` inside the loop.

4. **`server/DomainService/Workflow/Services/IWorkflowEngineService.cs`** — mirror the new signature for the interface (line 13).

5. **`server/DomainService/Workflow/Services/WorkflowExecutionService.cs` `StepExecuteAsync`** (line 802) — pass `dto.DestinationNodeId` through to `ExecuteStepNodeAsync` (line 921):
   ```csharp
   var result = await _workflowEngineService.ExecuteStepNodeAsync(
       dto.ProjectKey, execution.Id, triggerNode.Id,
       dto.NodeId, dto.DestinationNodeId, dto.SourceExecutionId);
   ```

## Files touched

| File | Change |
|------|--------|
| `server/DomainService/Workflow/Services/WorkflowEngineService.cs` | Add check in `ExecuteNodeAsync`; extend `ExecuteStepNodeAsync` signature + map `destinationNodeId` |
| `server/DomainService/Workflow/Services/IWorkflowEngineService.cs` | Update interface signature |
| `server/DomainService/Workflow/Dtos/StepExecuteRequestDto.cs` | Add `DestinationNodeId` property |
| `server/DomainService/Workflow/Services/WorkflowExecutionService.cs` | Pass `dto.DestinationNodeId` through in `StepExecuteAsync` |

## Validation steps

- Build with `dotnet build` (or whatever the repo uses — check `AGENTS.md` if present).
- Unit / smoke test:
  1. Workflow with 3 sequential nodes `A → B → C`. Send `StepExecute` for node `B` with `DestinationNodeId = "B-id"`. Expect: A runs, B runs, **C does not run**, execution `Status = Completed`, `FinishedAt` set, `ActiveNodeIds = []`.
  2. Same as above but `DestinationNodeId = null`. Expect: A, B, C all run (existing behavior).
  3. Force a failure at `B` (bad params) with `DestinationNodeId = "B-id"`. Expect: execution `Status = Failed`, no early-finalize side effects (failure path is unchanged).
- Confirm queue-mode (`RunNodeAsync` consumer): trigger workflow, send an `AddExcuationNodeEvent` with `destinationNodeId` populated, expect downstream queue messages are never enqueued.

## Risks / open items

- **Queue-mode wiring is out of scope here.** No caller currently populates `destinationNodeId` on `AddExcuationNodeEvent` for queue/in-process paths. This plan adds the behavior and wires it through `StepExecuteRequestDto`; producers of the queue payload (e.g. `WorkflowExecutionService` line 197 webhook flow) can set `destinationNodeId` in a follow-up if needed.
- **No validation that `destinationNodeId` matches an existing node.** The flag is treated as a "stop here, finalize" signal, not a target lookup. If a caller passes a bogus id, the execution still finalizes correctly — just at the wrong (current) node. Recommend documenting this behavior.
- **`StepExecuteAsync` already calls `AtomicFinalizeExecutionAsync` at line 508 after the loop.** If `DestinationNodeId` is set, the new logic will finalize inside the loop on the target node itself, and the existing post-loop finalize is redundant but harmless (idempotent). No change needed there.