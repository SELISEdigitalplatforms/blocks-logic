import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const svc = vi.hoisted(() => ({
  getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: { x: 1 } }),
  triggerListener: vi.fn().mockResolvedValue({}),
}));
vi.mock("../services/workflow.service", () => ({ workflowService: svc }));

import { makeHookWrapper } from "@/test-utils/test-providers/render";
import { useWorkflowNotification } from "./use-workflow-notification";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const seed = (nodeId: string) => (store: any) => {
  store.getState().setWorkflow({ itemId: "w1", name: "N" });
  store.getState().setIsListening(true, nodeId);
};

const emit = (detail: unknown) => {
  window.dispatchEvent(
    new CustomEvent("WorkflowNotification", { detail }),
  );
};

beforeEach(() => vi.clearAllMocks());

describe("useWorkflowNotification", () => {
  it("fetches execution data on a completed notification", async () => {
    renderHook(() => useWorkflowNotification(), {
      wrapper: makeHookWrapper(seed("trigger-1")),
    });
    const payload = JSON.stringify({
      Information: {
        code: "WF004",
        status: "done",
        executionId: "exec-1",
      },
    });
    await act(async () => {
      emit(JSON.stringify({ denormalizedPayload: payload }));
    });
    await waitFor(() =>
      expect(svc.getWorkflowExecutionById).toHaveBeenCalledWith({
        executionId: "exec-1",
      }),
    );
    expect(svc.triggerListener).toHaveBeenCalled();
  });

  it("ignores notifications when not listening", async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    renderHook(() => useWorkflowNotification(), {
      wrapper: makeHookWrapper((store: any) =>
        store.getState().setWorkflow({ itemId: "w1", name: "N" }),
      ),
    });
    await act(async () => {
      emit(JSON.stringify({ denormalizedPayload: "{}" }));
    });
    expect(svc.getWorkflowExecutionById).not.toHaveBeenCalled();
  });

  it("swallows malformed payloads without throwing", async () => {
    renderHook(() => useWorkflowNotification(), {
      wrapper: makeHookWrapper(seed("trigger-1")),
    });
    await act(async () => {
      emit("not-json");
    });
    expect(svc.getWorkflowExecutionById).not.toHaveBeenCalled();
  });
});
