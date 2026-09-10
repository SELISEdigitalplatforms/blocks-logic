import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const svc = vi.hoisted(() => ({ getWorkflowById: vi.fn() }));
const toasts = vi.hoisted(() => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn() }));
const download = vi.hoisted(() => ({ downloadJson: vi.fn() }));

vi.mock("../services/workflow.service", () => ({ workflowService: svc }));
vi.mock("@/hooks/use-toast", () => toasts);
vi.mock("../utils/download-json.util", () => download);

import { useExportWorkflow } from "./use-export-workflow";

beforeEach(() => vi.clearAllMocks());

describe("useExportWorkflow", () => {
  it("fetches the workflow and downloads a stripped, named JSON file (H2, H3)", async () => {
    svc.getWorkflowById.mockResolvedValue({
      isSuccess: true,
      data: {
        name: "My WF",
        settings: {},
        itemId: "src-1",
        isPublished: true,
        nodes: [
          {
            id: "8d75404dc42646619b91a7d85278687b",
            name: "Webhook",
            category: "trigger",
            type: "webhook",
            version: "v1",
            position: { x: 0, y: 0 },
            parameters: { path: "8d75404dc42646619b91a7d85278687b" },
            settings: {},
            pinData: [{ json: 1 }],
          },
        ],
        edges: [],
      },
    });

    const { result } = renderHook(() => useExportWorkflow());
    await act(async () => {
      await result.current.exportWorkflow("src-1");
    });

    expect(svc.getWorkflowById).toHaveBeenCalledWith({ id: "src-1" });
    expect(download.downloadJson).toHaveBeenCalledTimes(1);
    const [filename, payload] = download.downloadJson.mock.calls[0];
    expect(filename).toMatch(/^my-wf-\d{4}-\d{2}-\d{2}\.json$/);
    expect(Object.keys(payload).sort()).toEqual(
      ["description", "edges", "name", "nodes", "settings"].sort(),
    );
    expect(payload.nodes[0].pinData).toEqual([{ json: 1 }]);
    expect(toasts.showErrorToast).not.toHaveBeenCalled();
  });

  it("shows an error toast and downloads nothing when the fetch rejects (C8)", async () => {
    svc.getWorkflowById.mockRejectedValue(new Error("boom"));
    const { result } = renderHook(() => useExportWorkflow());
    await act(async () => {
      await result.current.exportWorkflow("src-1");
    });
    expect(download.downloadJson).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "Could not export this workflow. Please try again.",
    });
  });

  it("treats isSuccess=false as a failure (C8)", async () => {
    svc.getWorkflowById.mockResolvedValue({ isSuccess: false, data: null });
    const { result } = renderHook(() => useExportWorkflow());
    await act(async () => {
      await result.current.exportWorkflow("src-1");
    });
    expect(download.downloadJson).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalled();
  });
});
