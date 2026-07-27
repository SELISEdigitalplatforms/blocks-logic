import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const mutations = vi.hoisted(() => ({
  publishNew: vi.fn(),
  publish: vi.fn(),
  unpublish: vi.fn(),
}));
const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("./use-workflow-api", () => ({
  usePublishNewWorkflow: () => ({
    mutateAsync: mutations.publishNew,
    isPending: false,
  }),
  usePublishWorkflow: () => ({
    mutateAsync: mutations.publish,
    isPending: false,
  }),
  useUnpublishWorkflow: () => ({
    mutateAsync: mutations.unpublish,
    isPending: false,
  }),
}));
vi.mock("@/hooks/use-toast", () => toasts);

import { useWorkflowActions } from "./use-workflow-actions";

beforeEach(() => vi.clearAllMocks());

describe("useWorkflowActions", () => {
  it("handlePublishNew shows success and calls onSuccess", async () => {
    mutations.publishNew.mockResolvedValue({ isSuccess: true });
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useWorkflowActions());
    await act(async () => {
      await result.current.handlePublishNew("w1", "N", "D", onSuccess);
    });
    expect(mutations.publishNew).toHaveBeenCalledWith({
      workflowId: "w1",
      name: "N",
      description: "D",
    });
    expect(onSuccess).toHaveBeenCalled();
    expect(toasts.showSuccessToast).toHaveBeenCalled();
  });

  it("handlePublishNew surfaces a server failure", async () => {
    mutations.publishNew.mockResolvedValue({
      isSuccess: false,
      errors: { Message: "nope" },
    });
    const { result } = renderHook(() => useWorkflowActions());
    await act(async () => {
      await result.current.handlePublishNew("w1", "N", "D");
    });
    expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "nope" });
  });

  it("handlePublishNew catches thrown errors", async () => {
    mutations.publishNew.mockRejectedValue(new Error("boom"));
    const { result } = renderHook(() => useWorkflowActions());
    await act(async () => {
      await result.current.handlePublishNew("w1", "N", "D");
    });
    expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "boom" });
  });

  it("handlePublishUnversioned publishes with an optional version id", async () => {
    mutations.publish.mockResolvedValue({ isSuccess: true });
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useWorkflowActions());
    await act(async () => {
      await result.current.handlePublishUnversioned("w1", "v2", onSuccess);
    });
    expect(mutations.publish).toHaveBeenCalledWith({
      workflowId: "w1",
      versionId: "v2",
    });
    expect(onSuccess).toHaveBeenCalled();
  });

  it("handlePublishUnversioned reports a failure", async () => {
    mutations.publish.mockResolvedValue({ isSuccess: false, errors: {} });
    const { result } = renderHook(() => useWorkflowActions());
    await act(async () => {
      await result.current.handlePublishUnversioned("w1");
    });
    expect(toasts.showErrorToast).toHaveBeenCalled();
  });

  it("handleUnpublish shows success and catches errors", async () => {
    mutations.unpublish.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useWorkflowActions());
    await act(async () => {
      await result.current.handleUnpublish("w1");
    });
    expect(toasts.showSuccessToast).toHaveBeenCalled();

    mutations.unpublish.mockRejectedValue(new Error("x"));
    await act(async () => {
      await result.current.handleUnpublish("w1");
    });
    expect(toasts.showErrorToast).toHaveBeenCalled();
  });
});
