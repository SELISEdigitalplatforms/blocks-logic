import React, { useEffect } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
import { ReactFlowProvider } from "@xyflow/react";
import { TooltipProvider } from "@/components/ui-kits/tooltip/tooltip";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { WorkflowStoreProvider, useWorkflowStoreApi, type WorkflowStore } from "../../store";

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn(), showInfoToast: vi.fn() }));
const svc = vi.hoisted(() => ({
  getWorkflowById: vi.fn(),
  getLastSuccessfulExecution: vi.fn().mockResolvedValue(null),
  updateWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({ workflowService: svc }));
vi.mock("../../hooks/use-export-workflow", () => ({ useExportWorkflow: () => ({ exportWorkflow: vi.fn(), isExporting: false }) }));
vi.mock("../../components/workflow-editor", () => ({ WorkflowEditor: () => null }));
vi.mock("../../components/workflow-version", () => ({ WorkflowVersions: () => null }));
vi.mock("../../components/publish-workflow-action", () => ({ PublishWorkflowAction: () => null }));
vi.mock("@blocks-workflow/components/workflow-execution", () => ({ WorkflowExecutions: () => null }));

import { WorkflowDetailsContent } from "./workflow-details";

const KEY = "switch-handle-source-aaaaa1";
const switchNode = (handleLabel: string) => ({
  id: "s1",
  name: "Switch",
  category: "logic",
  type: "switch",
  version: "v2",
  position: { x: 0, y: 0 },
  data: {},
  settings: {},
  parameters: {
    mode: "rules",
    rules: [{ handleKey: KEY, handleLabel, conditionType: "all", conditions: [] }],
    options: { fallbackOutput: "none", renameFallbackOutput: "" },
  },
});
const serverCopy = (handleLabel: string, lastUpdatedDate = "2026-10-22T01:00:00Z") => ({
  isSuccess: true,
  data: { itemId: "wf1", name: "Flow", nodes: [switchNode(handleLabel)], edges: [], isDirty: false, isPublished: false, lastUpdatedDate },
});

const held: { store?: WorkflowStore } = {};
const Probe = () => {
  const api = useWorkflowStoreApi();
  useEffect(() => {
    held.store = api;
  }, [api]);
  return null;
};
const store = { getState: () => held.store!.getState() };

const renderPage = () => {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/p1/app/workflow/wf1"]}>
        <TooltipProvider>
          <Routes>
            <Route
              path="/:scope/app/workflow/:id"
              element={
                <ReactFlowProvider>
                  <WorkflowStoreProvider>
                    <Probe />
                    <WorkflowDetailsContent workflowId="wf1" />
                  </WorkflowStoreProvider>
                </ReactFlowProvider>
              }
            />
          </Routes>
        </TooltipProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return qc;
};

const label = () => (store.getState().nodesMap.s1.parameters as { rules: { handleLabel: string }[] }).rules[0].handleLabel;
const rename = (text: string) =>
  act(() => {
    const node = store.getState().nodesMap.s1;
    const parameters = node.parameters as { rules: object[] };
    store.getState().selectNode(node);
    store.getState().updateNodeParameters("s1", { ...parameters, rules: [{ ...(parameters.rules[0] as object), handleLabel: text }] });
  });

beforeEach(() => vi.clearAllMocks());

describe("WorkflowDetails reload", () => {
  it("a reload that lands while an edit is unsaved does not overwrite it; Save then sends the edit", async () => {
    svc.getWorkflowById.mockResolvedValue(serverCopy(""));
    const qc = renderPage();
    await waitFor(() => expect(store.getState().nodesMap.s1).toBeTruthy());

    rename("Test");
    expect(store.getState().hasUnsavedChanges).toBe(true);

    // A reload arrives with the server's older copy (an earlier save, or any invalidation).
    svc.getWorkflowById.mockResolvedValue(serverCopy("", "2026-10-22T01:05:00Z"));
    await act(async () => {
      await qc.invalidateQueries({ queryKey: ["workflow"] });
    });
    await waitFor(() => expect(svc.getWorkflowById).toHaveBeenCalledTimes(2));
    expect(label()).toBe("Test");
    expect(store.getState().hasUnsavedChanges).toBe(true);
    // The inspector's copy and the store's copy agree.
    expect((store.getState().selectedNode?.parameters as { rules: { handleLabel: string }[] }).rules[0].handleLabel).toBe("Test");

    svc.getWorkflowById.mockResolvedValue(serverCopy("Test", "2026-10-22T01:06:00Z"));
    await userEvent.setup().click(screen.getByRole("button", { name: /Save/ }));
    await waitFor(() => expect(svc.updateWorkflow).toHaveBeenCalledTimes(1));
    const sent = svc.updateWorkflow.mock.calls[0][0] as { nodes: { parameters: { rules: { handleLabel: string }[] } }[] };
    expect(sent.nodes[0].parameters.rules[0].handleLabel).toBe("Test");
    await waitFor(() => expect(store.getState().hasUnsavedChanges).toBe(false));
    expect(label()).toBe("Test");
  });

  it("an edit made while a save is in flight stays unsaved, and the reload after the save keeps it", async () => {
    svc.getWorkflowById.mockResolvedValue(serverCopy(""));
    renderPage();
    await waitFor(() => expect(store.getState().nodesMap.s1).toBeTruthy());

    rename("First");
    let finishSave: (v: unknown) => void = () => {};
    svc.updateWorkflow.mockImplementationOnce(() => new Promise((resolve) => (finishSave = resolve)));
    svc.getWorkflowById.mockResolvedValue(serverCopy("First", "2026-10-22T01:05:00Z"));
    await userEvent.setup().click(screen.getByRole("button", { name: /Save/ }));
    await waitFor(() => expect(svc.updateWorkflow).toHaveBeenCalledTimes(1));

    rename("Second");
    await act(async () => {
      finishSave({ isSuccess: true });
    });

    await waitFor(() => expect(svc.getWorkflowById.mock.calls.length).toBeGreaterThanOrEqual(2));
    expect(label()).toBe("Second");
    expect(store.getState().hasUnsavedChanges).toBe(true);
  });

  it("with nothing unsaved, a reload is applied as before", async () => {
    svc.getWorkflowById.mockResolvedValue(serverCopy(""));
    const qc = renderPage();
    await waitFor(() => expect(store.getState().nodesMap.s1).toBeTruthy());

    svc.getWorkflowById.mockResolvedValue(serverCopy("From server", "2026-10-22T01:05:00Z"));
    await act(async () => {
      await qc.invalidateQueries({ queryKey: ["workflow"] });
    });
    await waitFor(() => expect(label()).toBe("From server"));
    expect(store.getState().hasUnsavedChanges).toBe(false);
  });
});
