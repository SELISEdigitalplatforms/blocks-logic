import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const svc = vi.hoisted(() => ({
  updateWorkflow: vi.fn().mockResolvedValue({}),
  stepExecute: vi.fn(),
  triggerListener: vi.fn().mockResolvedValue({}),
  getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: { x: 1 } }),
}));
const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
vi.mock("../services/workflow.service", () => ({ workflowService: svc }));
vi.mock("@/hooks/use-toast", () => toasts);

import { makeHookWrapper } from "@/test-utils/test-providers/render";
import { useHandleExecuteStep } from "./use-handle-execute-step";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (id: string, category: string): any => ({
  id,
  name: id,
  type: category === "trigger" ? "webhook" : "action",
  category,
  position: { x: 0, y: 0 },
  parameters: {},
  data: {},
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const seedGraph = (triggerCount: number) => (store: any) => {
  const s = store.getState();
  s.setWorkflow({ itemId: "w1", name: "N" });
  s.addNode(node("action", "action"));
  for (let i = 0; i < triggerCount; i++) {
    s.addNode(node(`trigger${i}`, "trigger"));
    s.createEdge(
      { source: `trigger${i}`, sourceHandle: "main" },
      { target: "action", targetHandle: "in" },
    );
  }
};

beforeEach(() => vi.clearAllMocks());

describe("useHandleExecuteStep", () => {
  it("returns early without a workflow or node id", async () => {
    const { result } = renderHook(() => useHandleExecuteStep(), {
      wrapper: makeHookWrapper(),
    });
    await act(async () => {
      await result.current.handleExecuteStep(undefined);
    });
    expect(svc.updateWorkflow).not.toHaveBeenCalled();
  });

  it("requires an execution id when asked", async () => {
    const { result } = renderHook(() => useHandleExecuteStep(), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      wrapper: makeHookWrapper(seedGraph(1) as any),
    });
    await act(async () => {
      await result.current.handleExecuteStep("action", true);
    });
    expect(toasts.showErrorToast).toHaveBeenCalledWith(
      expect.objectContaining({ errors: "No successful execution found" }),
    );
  });

  it("saves, step-executes and stores the returned execution", async () => {
    svc.stepExecute.mockResolvedValue({ itemId: "exec-1" });
    const { result } = renderHook(() => useHandleExecuteStep(), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      wrapper: makeHookWrapper(seedGraph(1) as any),
    });
    await act(async () => {
      await result.current.handleExecuteStep("action");
    });
    expect(svc.updateWorkflow).toHaveBeenCalled();
    expect(svc.stepExecute).toHaveBeenCalled();
    await waitFor(() =>
      expect(svc.getWorkflowExecutionById).toHaveBeenCalledWith({
        executionId: "exec-1",
      }),
    );
  });

  it("starts listening on the single trigger predecessor", async () => {
    svc.stepExecute.mockResolvedValue({ code: "101" });
    const { result } = renderHook(() => useHandleExecuteStep(), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      wrapper: makeHookWrapper(seedGraph(1) as any),
    });
    await act(async () => {
      await result.current.handleExecuteStep("action");
    });
    await waitFor(() =>
      expect(svc.triggerListener).toHaveBeenCalledWith(
        expect.objectContaining({ TriggerId: "trigger0" }),
      ),
    );
  });

  it("opens the trigger selection modal when several triggers precede the node", async () => {
    svc.stepExecute.mockResolvedValue({ code: "101" });
    const { result } = renderHook(() => useHandleExecuteStep(), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      wrapper: makeHookWrapper(seedGraph(2) as any),
    });
    await act(async () => {
      await result.current.handleExecuteStep("action");
    });
    // the modal element is returned for rendering
    expect(result.current.executeStepModal).toBeTruthy();
  });

  it("shows an error toast when step execution throws", async () => {
    svc.stepExecute.mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useHandleExecuteStep(), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      wrapper: makeHookWrapper(seedGraph(1) as any),
    });
    await act(async () => {
      await result.current.handleExecuteStep("action");
    });
    expect(toasts.showErrorToast).toHaveBeenCalledWith(
      expect.objectContaining({ errors: "Failed to execute step" }),
    );
  });
});
