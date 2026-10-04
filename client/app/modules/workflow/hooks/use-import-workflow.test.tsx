import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const navigate = vi.hoisted(() => vi.fn());
const enqueue = vi.hoisted(() => vi.fn());
const storage = vi.hoisted(() => ({
  getPreSignedUrlForUpload: vi.fn(),
  uploadFileToPresignedUrl: vi.fn(),
}));
const toasts = vi.hoisted(() => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn() }));
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

import { useImportWorkflow } from "./use-import-workflow";

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
      errors: "This file is larger than the 5 MB limit.",
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
});
