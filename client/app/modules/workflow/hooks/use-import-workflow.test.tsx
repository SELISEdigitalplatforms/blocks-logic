import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const navigate = vi.hoisted(() => vi.fn());
const enqueue = vi.hoisted(() => vi.fn());
const storage = vi.hoisted(() => ({
  getPreSignedUrlForUpload: vi.fn(),
  uploadFileToPresignedUrl: vi.fn(),
  getWorkflows: vi.fn(),
}));
const toasts = vi.hoisted(() => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn(), showInfoToast: vi.fn() }));
const uuid = vi.hoisted(() => vi.fn(() => "cor-1"));

vi.mock("react-router", async (orig) => {
  const actual = (await orig()) as Record<string, unknown>;
  return { ...actual, useNavigate: () => navigate };
});
vi.mock("./use-workflow-api", () => ({
  useEnqueueWorkflowImport: () => ({ mutateAsync: enqueue }),
}));
vi.mock("../services/workflow.service", () => ({
  workflowService: storage,
}));
vi.mock("@/hooks/use-toast", () => toasts);
vi.mock("uuid", () => ({ v4: uuid }));

import { IMPORT_POLL_MS, IMPORT_POLL_TRIES, useImportWorkflow } from "./use-import-workflow";

const fileOf = (obj: unknown, opts: { size?: number } = {}) => {
  const text = typeof obj === "string" ? obj : JSON.stringify(obj);
  const file = new File([text], "wf.json", { type: "application/json" });
  if (opts.size !== undefined) Object.defineProperty(file, "size", { value: opts.size });
  return file;
};

const goodRoot = {
  name: "wf",
  settings: {},
  nodes: [],
  edges: [],
};

const run = async (file: File) => {
  const { result } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
  await act(async () => {
    await result.current.importWorkflow(file);
  });
  return result;
};

const emitImport = (detail: unknown) => {
  window.dispatchEvent(new CustomEvent("workflow-import", { detail }));
};

beforeEach(() => {
  vi.clearAllMocks();
  uuid.mockReturnValue("cor-1");
  storage.getPreSignedUrlForUpload.mockResolvedValue({
    isSuccess: true,
    fileId: "file-1",
    uploadUrl: "https://blob.example/upload",
  });
  storage.uploadFileToPresignedUrl.mockResolvedValue(undefined);
  enqueue.mockResolvedValue({ isSuccess: true });
  storage.getWorkflows.mockResolvedValue({ data: [] });
});

describe("useImportWorkflow", () => {
  it("uploads the file, enqueues import, and toasts started", async () => {
    await run(fileOf(goodRoot));

    expect(storage.getPreSignedUrlForUpload).toHaveBeenCalledTimes(1);
    expect(storage.uploadFileToPresignedUrl).toHaveBeenCalledWith(
      "https://blob.example/upload",
      expect.any(File),
    );
    expect(enqueue).toHaveBeenCalledWith({
      fileId: "file-1",
      messageCoRelationId: "cor-1",
    });
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({
      description: "Import started. You'll be notified when it's ready.",
    });
    expect(navigate).not.toHaveBeenCalled();
  });

  it("navigates when the matching import notification succeeds", async () => {
    const { result } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
    await act(async () => {
      await result.current.importWorkflow(fileOf(goodRoot));
    });

    await act(async () => {
      emitImport({
        responseKey: "cor-1",
        denormalizedPayload: JSON.stringify({
          Message: { IsSuccess: true, workflowId: "NEW1", issues: 0, description: "Your workflow is ready." },
        }),
      });
    });

    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Workflow imported." });
    expect(navigate).toHaveBeenCalledWith("workflow/NEW1");
  });

  it("reports skipped items from the notification", async () => {
    const { result } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
    await act(async () => {
      await result.current.importWorkflow(fileOf(goodRoot));
    });

    await act(async () => {
      emitImport({
        ResponseKey: "cor-1",
        denormalizedPayload: JSON.stringify({
          Message: { IsSuccess: true, workflowId: "NEW1", issues: 2 },
        }),
      });
    });

    expect(toasts.showSuccessToast).toHaveBeenCalledWith({
      description:
        "Workflow imported. 2 item(s) were skipped because they were invalid or disconnected.",
    });
  });

  it("aborts oversized files before any upload (C1)", async () => {
    await run(fileOf(goodRoot, { size: 5 * 1024 * 1024 + 1 }));
    expect(storage.getPreSignedUrlForUpload).not.toHaveBeenCalled();
    expect(enqueue).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "This file is too large.",
    });
  });

  it("rejects invalid JSON (C2)", async () => {
    await run(fileOf("not json"));
    expect(enqueue).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "This file is not valid JSON." });
  });

  it("rejects a file missing required fields (C3)", async () => {
    await run(fileOf({ foo: 1 }));
    expect(enqueue).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "This file is not a valid workflow export (missing name, nodes, edges or settings).",
    });
  });

  it("on upload failure shows a toast and does not enqueue", async () => {
    storage.getPreSignedUrlForUpload.mockResolvedValue({ isSuccess: false });
    await run(fileOf(goodRoot));
    expect(enqueue).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "Could not upload the workflow file. Please try again.",
    });
  });

  it("on enqueue failure shows a toast and does not navigate", async () => {
    enqueue.mockResolvedValue({ isSuccess: false });
    await run(fileOf(goodRoot));
    expect(navigate).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "Could not start the import. Please try again.",
    });
  });

  it("shows an error toast when the import notification reports failure", async () => {
    const { result } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
    await act(async () => {
      await result.current.importWorkflow(fileOf(goodRoot));
    });

    await act(async () => {
      emitImport({
        responseKey: "cor-1",
        denormalizedPayload: JSON.stringify({
          Message: { IsSuccess: false, description: "The import file could not be found." },
        }),
      });
    });

    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "The import file could not be found.",
    });
    expect(navigate).not.toHaveBeenCalled();
  });

  it("exposes an in-flight flag (C9)", async () => {
    let resolveUpload: (v: unknown) => void = () => {};
    storage.getPreSignedUrlForUpload.mockReturnValue(new Promise((r) => (resolveUpload = r)));
    const { result } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
    let p: Promise<void>;
    act(() => {
      p = result.current.importWorkflow(fileOf(goodRoot));
    });
    await waitFor(() => expect(result.current.isImporting).toBe(true));
    await act(async () => {
      resolveUpload({ isSuccess: true, fileId: "file-1", uploadUrl: "https://blob.example/upload" });
      await p;
    });
    expect(result.current.isImporting).toBe(false);
  });

  it("finishes on the WorkflowNotification event, the channel the server sends on", async () => {
    await run(fileOf(goodRoot));

    await act(async () => {
      window.dispatchEvent(new CustomEvent("WorkflowNotification", {
        detail: {
          method: "WorkflowNotification",
          message: {
            responseKey: "cor-1",
            denormalizedPayload: JSON.stringify({ Message: { IsSuccess: true, workflowId: "NEW1", issues: 0 } }),
          },
        },
      }));
    });

    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Workflow imported." });
    expect(navigate).toHaveBeenCalledWith("workflow/NEW1");
  });

  it("ignores execution events on the same channel", async () => {
    await run(fileOf(goodRoot));

    await act(async () => {
      window.dispatchEvent(new CustomEvent("WorkflowNotification", {
        detail: { message: { responseKey: "WorkflowExecution", denormalizedPayload: JSON.stringify({ Information: { code: "WF004" } }) } },
      }));
    });

    expect(navigate).not.toHaveBeenCalled();
  });

  describe("without a notification", () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    const tick = () => act(async () => { await vi.advanceTimersByTimeAsync(IMPORT_POLL_MS); });
    const named = (...ids: string[]) => ({ data: ids.map((itemId) => ({ itemId, name: "wf" })) });

    it("finds the new workflow in the list, not an older one with the same name", async () => {
      storage.getWorkflows
        .mockResolvedValueOnce(named("OLD"))          // before the upload
        .mockResolvedValueOnce(named("OLD"))          // not there yet
        .mockResolvedValue(named("NEW2", "OLD"));     // imported
      await run(fileOf(goodRoot));

      await tick();
      expect(navigate).not.toHaveBeenCalled();
      await tick();

      expect(navigate).toHaveBeenCalledWith("workflow/NEW2");
      expect(toasts.showSuccessToast).toHaveBeenLastCalledWith({ description: "Workflow imported." });
    });

    it("says it is taking longer at the end of the window, never that it failed", async () => {
      storage.getWorkflows.mockResolvedValue(named("OLD"));
      await run(fileOf(goodRoot));

      for (let i = 0; i < IMPORT_POLL_TRIES; i++) await tick();

      expect(navigate).not.toHaveBeenCalled();
      expect(toasts.showErrorToast).not.toHaveBeenCalled();
      expect(toasts.showInfoToast).toHaveBeenCalledWith({
        description: "The import is taking longer than expected. Check the workflow list again in a moment.",
      });
      const calls = storage.getWorkflows.mock.calls.length;
      await tick();
      expect(storage.getWorkflows.mock.calls.length).toBe(calls);
    });

    it("stops checking once the notification has finished it (one success, one navigation)", async () => {
      storage.getWorkflows.mockResolvedValueOnce(named()).mockResolvedValue(named("NEW1"));
      await run(fileOf(goodRoot));

      await act(async () => {
        emitImport({ responseKey: "cor-1", denormalizedPayload: JSON.stringify({ Message: { IsSuccess: true, workflowId: "NEW1" } }) });
      });
      await tick();

      expect(navigate).toHaveBeenCalledTimes(1);
      expect(storage.getWorkflows).toHaveBeenCalledTimes(1);
    });

    it("keeps checking when one check fails", async () => {
      storage.getWorkflows
        .mockResolvedValueOnce(named())
        .mockRejectedValueOnce(new Error("network"))
        .mockResolvedValue(named("NEW3"));
      await run(fileOf(goodRoot));

      await tick();
      await tick();

      expect(navigate).toHaveBeenCalledWith("workflow/NEW3");
    });

    it("does not check at all when the list could not be read first (it could pick the wrong one)", async () => {
      storage.getWorkflows.mockRejectedValueOnce(new Error("down")).mockResolvedValue(named("ANY"));
      await run(fileOf(goodRoot));

      await tick();

      expect(storage.getWorkflows).toHaveBeenCalledTimes(1);
      expect(navigate).not.toHaveBeenCalled();
    });

    it("searches the name literally (the list search is a regex on the server)", async () => {
      await run(fileOf({ ...goodRoot, name: "Orders (v2).*" }));

      expect(storage.getWorkflows).toHaveBeenCalledWith({ search: "Orders \\(v2\\)\\.\\*", pageNumber: 0, pageSize: 50 });
    });

    it("stops when the page is left", async () => {
      storage.getWorkflows.mockResolvedValueOnce(named()).mockResolvedValue(named("NEW4"));
      const { result, unmount } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
      await act(async () => {
        await result.current.importWorkflow(fileOf(goodRoot));
      });
      unmount();

      await tick();

      expect(navigate).not.toHaveBeenCalled();
    });
  });
});

