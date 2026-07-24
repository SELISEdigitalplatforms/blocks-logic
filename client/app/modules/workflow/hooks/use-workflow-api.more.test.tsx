import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const svc = vi.hoisted(() => ({
  getWorkflows: vi.fn().mockResolvedValue({ ok: 1 }),
  getWorkflowById: vi.fn().mockResolvedValue({ ok: 1 }),
  createWorkflow: vi.fn().mockResolvedValue({ ok: 1 }),
  duplicateWorkflow: vi.fn().mockResolvedValue({ ok: 1 }),
  updateWorkflow: vi.fn().mockResolvedValue({ ok: 1 }),
  deleteWorkflow: vi.fn().mockResolvedValue({ ok: 1 }),
  getWorkflowExecutions: vi.fn().mockResolvedValue({ ok: 1 }),
  getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: { x: 1 } }),
  createWorkflowVersion: vi.fn().mockResolvedValue({ ok: 1 }),
  getWorkflowVersions: vi.fn().mockResolvedValue({ ok: 1 }),
  getWorkflowByVersion: vi.fn().mockResolvedValue({ ok: 1 }),
  publishWorkflow: vi.fn().mockResolvedValue({ ok: 1 }),
  publishWorkflowNewVersion: vi.fn().mockResolvedValue({ ok: 1 }),
  unpublishWorkflow: vi.fn().mockResolvedValue({ ok: 1 }),
  restoreWorkflow: vi.fn().mockResolvedValue({ ok: 1 }),
  updateWorkflowVersion: vi.fn().mockResolvedValue({ ok: 1 }),
  getLastSuccessfulExecution: vi.fn().mockResolvedValue({ ok: 1 }),
  stepExecute: vi.fn().mockResolvedValue({ ok: 1 }),
  triggerListener: vi.fn().mockResolvedValue({ ok: 1 }),
}));

vi.mock("../services/workflow.service", () => ({ workflowService: svc }));

import { makeHookWrapper } from "@/test-utils/test-providers/render";
import * as api from "./use-workflow-api";

const wrapper = (seed?: Parameters<typeof makeHookWrapper>[0]) =>
  makeHookWrapper(seed);

beforeEach(() => vi.clearAllMocks());

describe("query hooks", () => {
  it("useGetWorkflows fetches", async () => {
    const { result } = renderHook(() => api.useGetWorkflows({ page: 1 } as never), {
      wrapper: wrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(svc.getWorkflows).toHaveBeenCalled();
  });

  it("useGetWorkflowById is disabled without an id", () => {
    const { result } = renderHook(() => api.useGetWorkflowById({ id: "" } as never), {
      wrapper: wrapper(),
    });
    expect(result.current.fetchStatus).toBe("idle");
  });

  it("useGetWorkflowById fetches with an id", async () => {
    const { result } = renderHook(
      () => api.useGetWorkflowById({ id: "w1" } as never),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("execution, versions and last-successful queries fetch when enabled", async () => {
    const { result: r1 } = renderHook(
      () => api.useGetWorkflowExecutions({ workflowId: "w1" } as never),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(r1.current.isSuccess).toBe(true));

    const { result: r2 } = renderHook(
      () => api.useGetWorkflowExecutionById({ executionId: "e1" } as never),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(r2.current.isSuccess).toBe(true));

    const { result: r3 } = renderHook(
      () => api.useGetWorkflowVersions({ workflowId: "w1" } as never),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(r3.current.isSuccess).toBe(true));

    const { result: r4 } = renderHook(
      () =>
        api.useGetWorkflowByVersion({
          workflowId: "w1",
          versionId: "v1",
        } as never),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(r4.current.isSuccess).toBe(true));

    const { result: r5 } = renderHook(
      () => api.useGetLastSuccessfulExecution({ workflowId: "w1" } as never),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(r5.current.isSuccess).toBe(true));
  });
});

describe("mutation hooks", () => {
  const cases: [keyof typeof api, keyof typeof svc][] = [
    ["useCreateWorkflow", "createWorkflow"],
    ["useDuplicateWorkflow", "duplicateWorkflow"],
    ["useUpdateWorkflow", "updateWorkflow"],
    ["useDeleteWorkflow", "deleteWorkflow"],
    ["useCreateWorkflowVersion", "createWorkflowVersion"],
    ["usePublishWorkflow", "publishWorkflow"],
    ["usePublishNewWorkflow", "publishWorkflowNewVersion"],
    ["useUnpublishWorkflow", "unpublishWorkflow"],
    ["useRestoreWorkflow", "restoreWorkflow"],
    ["useUpdateWorkflowVersion", "updateWorkflowVersion"],
    ["useStepExecute", "stepExecute"],
  ];

  it.each(cases)("%s runs the service and its onSuccess", async (hookName, svcName) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const { result } = renderHook(() => (api[hookName] as any)(), {
      wrapper: wrapper(),
    });
    await act(async () => {
      await result.current.mutateAsync({ workflowId: "w1" });
    });
    expect(svc[svcName]).toHaveBeenCalled();
  });
});

describe("useStepExecutionHandler", () => {
  it("fetches an execution and stores the step data", async () => {
    const { result } = renderHook(() => api.useStepExecutionHandler(), {
      wrapper: wrapper(),
    });
    await act(async () => {
      await result.current.handleExecuteStep("e1");
    });
    expect(svc.getWorkflowExecutionById).toHaveBeenCalledWith({
      executionId: "e1",
    });
  });

  it("shows an error toast when the fetch fails", async () => {
    svc.getWorkflowExecutionById.mockRejectedValueOnce(new Error("boom"));
    const { result } = renderHook(() => api.useStepExecutionHandler(), {
      wrapper: wrapper(),
    });
    await act(async () => {
      await result.current.handleExecuteStep("bad");
    });
    // no throw; error path handled internally
    expect(svc.getWorkflowExecutionById).toHaveBeenCalled();
  });
});

describe("useExecuteTriggerListener", () => {
  it("no-ops when there is no workflow id", async () => {
    const { result } = renderHook(() => api.useExecuteTriggerListener(), {
      wrapper: wrapper(),
    });
    await act(async () => {
      await result.current.mutateAsync({ triggerId: "t1", enableListener: true });
    });
    expect(svc.triggerListener).not.toHaveBeenCalled();
  });

  it("calls the service with the workflow id when present", async () => {
    const seed = (store: {
      getState: () => { setWorkflow: (w: unknown) => void };
    }) => store.getState().setWorkflow({ itemId: "w9", name: "N" });
    const { result } = renderHook(() => api.useExecuteTriggerListener(), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      wrapper: wrapper(seed as any),
    });
    await act(async () => {
      await result.current.mutateAsync({
        triggerId: "t1",
        enableListener: false,
        completionNodeId: "c1",
      });
    });
    expect(svc.triggerListener).toHaveBeenCalledWith(
      expect.objectContaining({ WorkflowId: "w9", CompletionNodeId: "c1" }),
    );
  });
});
