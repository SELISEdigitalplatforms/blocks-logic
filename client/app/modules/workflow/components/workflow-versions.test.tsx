import React from "react";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ReactFlowProvider } from "@xyflow/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { WorkflowStoreProvider } from "../store";
import { TooltipProvider } from "@/components/ui-kits/tooltip/tooltip";
import { WorkflowVersionEditor } from "./workflow-version/workflow-version-editor";
import { WorkflowVersions } from "./workflow-version/workflow-version";

const svc = vi.hoisted(() => ({
  getWorkflowByVersion: vi.fn(),
  getWorkflowVersions: vi.fn().mockResolvedValue({ data: [] }),
  triggerListener: vi.fn().mockResolvedValue({}),
  restoreWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  updateWorkflowVersion: vi.fn().mockResolvedValue({ isSuccess: true }),
  publishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
  unpublishWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));

beforeEach(() => vi.clearAllMocks());

describe("WorkflowVersionEditor", () => {
  it("prompts to select a version when none is given", () => {
    renderWithProviders(<WorkflowVersionEditor />);
    expect(screen.getByText("Select a version to view details")).toBeTruthy();
  });

  it("renders the version snapshot once fetched", async () => {
    svc.getWorkflowByVersion.mockResolvedValue({
      data: {
        nodes: [
          {
            id: "n1",
            type: "webhook",
            category: "trigger",
            version: "v1",
            name: "n1",
            position: { x: 0, y: 0 },
            parameters: {},
            data: {},
          },
        ],
        edges: [],
      },
    });
    render(
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
      >
        <MemoryRouter initialEntries={["/workflow/w1"]}>
          <Routes>
            <Route
              path="/workflow/:id"
              element={
                <TooltipProvider>
                  <ReactFlowProvider>
                    <WorkflowStoreProvider>
                      <WorkflowVersionEditor version={{ itemId: "v1", name: "V1" }} />
                    </WorkflowStoreProvider>
                  </ReactFlowProvider>
                </TooltipProvider>
              }
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
    await waitFor(() =>
      expect(svc.getWorkflowByVersion).toHaveBeenCalledWith({
        workflowId: "w1",
        versionId: "v1",
      }),
    );
    await waitFor(() => expect(screen.getByText("Version:")).toBeTruthy());
  });
});

describe("WorkflowVersions", () => {
  it("renders the version sidebar and editor", async () => {
    render(
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
      >
        <MemoryRouter initialEntries={["/workflow/w1"]}>
          <Routes>
            <Route path="/workflow/:id" element={<WorkflowVersions sidebarPosition="left" />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
    await waitFor(() =>
      expect(svc.getWorkflowVersions).toHaveBeenCalledWith({ workflowId: "w1" }),
    );
    expect(screen.getByText("Select a version to view details")).toBeTruthy();
  });
});
