import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";
import { TooltipProvider } from "@/components/ui-kits/tooltip/tooltip";
import { beforeEach, describe, expect, it, vi } from "vitest";

const svc = vi.hoisted(() => ({
  getWorkflows: vi.fn(),
  getWorkflowById: vi.fn(),
  getLastSuccessfulExecution: vi.fn().mockResolvedValue({ data: {} }),
  triggerListener: vi.fn().mockResolvedValue({}),
  updateWorkflow: vi.fn().mockResolvedValue({ isSuccess: true }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));

import { Workflows } from "./workflows/workflows";
import { WorkflowDetails } from "./workflow-details/workflow-details";

const providers = (ui: React.ReactElement, entry = "/workflow") => {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[entry]}>
        <NuqsTestingAdapter>
          <TooltipProvider>
            <Routes>
              <Route path="/workflow" element={ui} />
              <Route path="/workflow/:id" element={ui} />
            </Routes>
          </TooltipProvider>
        </NuqsTestingAdapter>
      </MemoryRouter>
    </QueryClientProvider>,
  );
};

beforeEach(() => vi.clearAllMocks());

describe("Workflows page", () => {
  it("renders the workflow list and pagination", async () => {
    svc.getWorkflows.mockResolvedValue({
      data: [
        {
          itemId: "w1",
          name: "Flow 1",
          createdDate: "2023-01-01T00:00:00Z",
          lastUpdatedDate: "2023-01-02T00:00:00Z",
          isPublished: false,
        },
      ],
      totalCount: 1,
    });
    providers(<Workflows />);
    expect(screen.getByText("Workflow")).toBeTruthy();
    await waitFor(() => expect(svc.getWorkflows).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText("Flow 1")).toBeTruthy());
  });
});

describe("WorkflowDetails page", () => {
  it("shows a loader then the editor tabs once the workflow loads", async () => {
    svc.getWorkflowById.mockResolvedValue({
      data: {
        itemId: "w1",
        name: "My Flow",
        isPublished: false,
        isDirty: false,
        nodes: [],
        edges: [],
        lastUpdatedDate: "2023-01-01T00:00:00Z",
      },
    });
    providers(<WorkflowDetails />, "/workflow/w1");
    await waitFor(() => expect(svc.getWorkflowById).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText("Editor")).toBeTruthy());
    expect(screen.getByText("Executions")).toBeTruthy();
    expect(screen.getByText("Versions")).toBeTruthy();
  });

  it("dismisses the unadapted changes banner", async () => {
    svc.getWorkflowById.mockResolvedValue({
      data: {
        itemId: "w1",
        name: "My Flow",
        isPublished: false,
        isDirty: true,
        nodes: [],
        edges: [],
        lastUpdatedDate: "2023-01-01T00:00:00Z",
      },
    });
    providers(<WorkflowDetails />, "/workflow/w1");

    await waitFor(() =>
      expect(screen.getByText(/You have unadapted changes/)).toBeTruthy(),
    );
    fireEvent.click(screen.getByLabelText("Dismiss unadapted changes warning"));

    expect(screen.queryByText(/You have unadapted changes/)).toBeNull();
  });
});
