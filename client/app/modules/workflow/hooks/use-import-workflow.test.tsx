import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const navigate = vi.hoisted(() => vi.fn());
const mutations = vi.hoisted(() => ({ create: vi.fn(), update: vi.fn() }));
const toasts = vi.hoisted(() => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn() }));

vi.mock("react-router", async (orig) => {
  const actual = (await orig()) as Record<string, unknown>;
  return { ...actual, useNavigate: () => navigate };
});
vi.mock("./use-workflow-api", () => ({
  useCreateWorkflow: () => ({ mutateAsync: mutations.create }),
  useUpdateWorkflow: () => ({ mutateAsync: mutations.update }),
}));
vi.mock("@/hooks/use-toast", () => toasts);

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
  nodes: [
    {
      id: "8d75404dc42646619b91a7d85278687b",
      name: "Webhook",
      type: "webhook",
      category: "trigger",
      version: "v1",
      position: { x: 0, y: 0 },
      parameters: { path: "8d75404dc42646619b91a7d85278687b" },
      settings: {},
    },
    {
      id: "bcc3fabc012345678901234567890abc",
      name: "HTTP",
      type: "httpRequest",
      category: "action",
      version: "v1",
      position: { x: 200, y: 0 },
      parameters: {},
      settings: {},
    },
  ],
  edges: [
    {
      id: "xy-edge__8d75404dc42646619b91a7d85278687b-bcc3fabc012345678901234567890abc",
      source: "8d75404dc42646619b91a7d85278687b",
      target: "bcc3fabc012345678901234567890abc",
      sourceHandle: "source",
      targetHandle: "target",
    },
  ],
};

const run = async (file: File) => {
  const { result } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
  await act(async () => {
    await result.current.importWorkflow(file);
  });
  return result;
};

beforeEach(() => {
  vi.clearAllMocks();
  mutations.create.mockResolvedValue({ isSuccess: true, itemId: "NEW1" });
  mutations.update.mockResolvedValue({ isSuccess: true });
});

describe("useImportWorkflow", () => {
  it("calls Create then Update with the right payloads and navigates (H5, H8, Example 2)", async () => {
    await run(fileOf(goodRoot));

    expect(mutations.create).toHaveBeenCalledWith({ name: "wf" });
    expect(mutations.update).toHaveBeenCalledTimes(1);
    const payload = mutations.update.mock.calls[0][0];
    expect(payload.itemId).toBe("NEW1");
    expect(payload.nodes).toHaveLength(2);
    expect(payload.nodes[0].id).toMatch(/^[0-9a-f]{32}$/);
    expect(payload.nodes[0].id).not.toBe("8d75404dc42646619b91a7d85278687b");
    expect(payload.nodes[0].parameters.path).toBe(payload.nodes[0].id);
    expect(payload.edges[0]).toMatchObject({
      source: payload.nodes[0].id,
      target: payload.nodes[1].id,
      id: `xy-edge__${payload.nodes[0].id}-${payload.nodes[1].id}`,
      sourceHandle: "source",
      targetHandle: "target",
    });
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Workflow imported." });
    expect(navigate).toHaveBeenCalledWith("workflow/NEW1");
  });

  it("reports skipped entities in the success toast (H7, Example 5)", async () => {
    const root = {
      ...goodRoot,
      nodes: [goodRoot.nodes[0], { ...goodRoot.nodes[0], name: "dup" }, goodRoot.nodes[1]],
      edges: [
        goodRoot.edges[0],
        {
          source: "bcc3fabc012345678901234567890abc",
          target: "does-not-exist",
          sourceHandle: "s",
          targetHandle: "t",
        },
      ],
    };
    await run(fileOf(root));
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({
      description:
        "Workflow imported. 2 item(s) were skipped because they were invalid or disconnected.",
    });
  });

  it("aborts oversized files before any Create call (C1)", async () => {
    await run(fileOf(goodRoot, { size: 5 * 1024 * 1024 + 1 }));
    expect(mutations.create).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "This file is larger than the 5 MB limit.",
    });
  });

  it("rejects invalid JSON (C2)", async () => {
    await run(fileOf("not json"));
    expect(mutations.create).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "This file is not valid JSON." });
  });

  it("rejects a file missing required fields (C3, Example 3)", async () => {
    await run(fileOf({ foo: 1 }));
    expect(mutations.create).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "This file is not a valid workflow export (missing name, nodes, edges or settings).",
    });
  });

  it("on Create failure shows a toast and makes no Update call (C6)", async () => {
    mutations.create.mockResolvedValue({ isSuccess: false });
    await run(fileOf(goodRoot));
    expect(mutations.update).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "Could not create the workflow. Please try again.",
    });
  });

  it("on Update failure shows a toast but still navigates to the new workflow (C7, Example 7)", async () => {
    mutations.update.mockRejectedValue(new Error("500"));
    await run(fileOf(goodRoot));
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "The workflow was created but its contents could not be saved.",
    });
    expect(navigate).toHaveBeenCalledWith("workflow/NEW1");
  });

  it("exposes an in-flight flag (C9)", async () => {
    let resolveCreate: (v: unknown) => void = () => {};
    mutations.create.mockReturnValue(new Promise((r) => (resolveCreate = r)));
    const { result } = renderHook(() => useImportWorkflow(), { wrapper: createWrapper() });
    let p: Promise<void>;
    act(() => {
      p = result.current.importWorkflow(fileOf(goodRoot));
    });
    await waitFor(() => expect(result.current.isImporting).toBe(true));
    await act(async () => {
      resolveCreate({ isSuccess: true, itemId: "NEW1" });
      await p;
    });
    expect(result.current.isImporting).toBe(false);
  });
});
