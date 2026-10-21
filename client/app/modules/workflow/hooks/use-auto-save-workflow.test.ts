import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockWorkflowServiceFactory } from "../test-utils/__mocks__";
import { mockWorkflowNode1 } from "../test-utils/__mocks__";
import { workflowService } from "../services/workflow.service";
import { mockUpdateWorkflowResponse, MOCK_WORKFLOW_ID_1 } from "../test-utils/__mocks__";
import { useAutoSaveWorkflow } from "./use-auto-save-workflow";
import type { Edge } from "@xyflow/react";
import { mockWorkflowEdge1 } from "../test-utils/__mocks__";

vi.mock("../services/workflow.service", () => mockWorkflowServiceFactory());

const { mockGetState, mockUseWorkflowStore, mockMarkSaved } = vi.hoisted(() => {
  const mockGetState = vi.fn();
  const mockMarkSaved = vi.fn();
  const mockUseWorkflowStore = vi.fn(
    (selector: (state: { hasUnsavedChanges: boolean }) => boolean) =>
      selector({ hasUnsavedChanges: true }),
  );
  return { mockGetState, mockUseWorkflowStore, mockMarkSaved };
});

vi.mock("../store", () => ({
  useWorkflowStore: mockUseWorkflowStore,
  useWorkflowStoreApi: () => ({ getState: mockGetState }),
}));

const DEFAULT_OPTIONS = {
  workflowId: MOCK_WORKFLOW_ID_1,
  debounceMs: 500,
};

describe("useAutoSaveWorkflow", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    vi.mocked(workflowService.updateWorkflow).mockResolvedValue(mockUpdateWorkflowResponse);
    // Restore default hasUnsavedChanges: true for each test
    mockUseWorkflowStore.mockImplementation(
      (selector: (state: { hasUnsavedChanges: boolean }) => boolean) =>
        selector({ hasUnsavedChanges: true }),
    );
    mockGetState.mockReturnValue({
      nodesMap: { [mockWorkflowNode1.id]: mockWorkflowNode1 },
      edgesMap: { [mockWorkflowEdge1.id]: mockWorkflowEdge1 as Edge },
      nodeOutputSchemas: {},
      markSaved: mockMarkSaved,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("should return isSaving as false initially", () => {
    const { result } = renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), {
      wrapper: createWrapper(),
    });

    expect(result.current.isSaving).toBe(false);
  });

  it("should expose a saveNow function", () => {
    const { result } = renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), {
      wrapper: createWrapper(),
    });

    expect(typeof result.current.saveNow).toBe("function");
  });

  it("should call updateWorkflow after the debounce timer fires", async () => {
    renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), { wrapper: createWrapper() });

    // Not called before debounce expires
    expect(workflowService.updateWorkflow).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    expect(workflowService.updateWorkflow).toHaveBeenCalledWith(
      expect.objectContaining({
        itemId: MOCK_WORKFLOW_ID_1,
      }),
      expect.anything(),
    );
  });

  it("should pass nodes and edges from the store to updateWorkflow", async () => {
    renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), { wrapper: createWrapper() });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    expect(workflowService.updateWorkflow).toHaveBeenCalledWith(
      expect.objectContaining({
        nodes: expect.arrayContaining([expect.objectContaining({ id: mockWorkflowNode1.id })]),
        edges: expect.arrayContaining([expect.objectContaining({ id: mockWorkflowEdge1.id })]),
      }),
      expect.anything(),
    );
  });

  it("H9: sends only the whitelisted node fields", async () => {
    renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), { wrapper: createWrapper() });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    const payload = vi.mocked(workflowService.updateWorkflow).mock.calls[0][0];
    const sent = payload.nodes!.find((n) => n.id === mockWorkflowNode1.id)!;
    expect(Object.keys(sent).every((key) =>
      ["id", "name", "category", "type", "version", "position", "handle", "parameters", "settings", "pinData"].includes(key),
    )).toBe(true);
    expect(sent).not.toHaveProperty("data");
    expect(sent).not.toHaveProperty("description");
  });

  it("should call updateWorkflow immediately on saveNow()", async () => {
    const { result } = renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await result.current.saveNow();
    });

    expect(workflowService.updateWorkflow).toHaveBeenCalledTimes(1);
  });

  it("should not trigger auto-save when enabled is false", async () => {
    // Override store mock so isDirty is true but enabled=false
    renderHook(() => useAutoSaveWorkflow({ ...DEFAULT_OPTIONS, enabled: false }), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    expect(workflowService.updateWorkflow).not.toHaveBeenCalled();
  });

  it("should not trigger auto-save when hasUnsavedChanges is false", async () => {
    // Override store to return hasUnsavedChanges: false
    mockUseWorkflowStore.mockImplementation(
      (selector: (state: { hasUnsavedChanges: boolean }) => boolean) =>
        selector({ hasUnsavedChanges: false }),
    );

    renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), { wrapper: createWrapper() });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    expect(workflowService.updateWorkflow).not.toHaveBeenCalled();
  });

  it("should call onSaveSuccess callback after successful save", async () => {
    const onSaveSuccess = vi.fn();
    renderHook(() => useAutoSaveWorkflow({ ...DEFAULT_OPTIONS, onSaveSuccess }), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    expect(onSaveSuccess).toHaveBeenCalledTimes(1);
  });

  it("marks the sent nodes and edges as saved after a successful save", async () => {
    renderHook(() => useAutoSaveWorkflow(DEFAULT_OPTIONS), { wrapper: createWrapper() });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    const sent = mockGetState.mock.results[0].value as { nodesMap: object; edgesMap: object };
    expect(mockMarkSaved).toHaveBeenCalledWith(sent.nodesMap, sent.edgesMap);
  });

  it("should call onSaveError callback when updateWorkflow rejects", async () => {
    const error = new Error("Network error");
    vi.mocked(workflowService.updateWorkflow).mockRejectedValue(error);
    const onSaveError = vi.fn();

    renderHook(() => useAutoSaveWorkflow({ ...DEFAULT_OPTIONS, onSaveError }), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_OPTIONS.debounceMs + 100);
    });

    expect(onSaveError).toHaveBeenCalledWith(error);
  });
});
