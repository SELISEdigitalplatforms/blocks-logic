import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { WorkflowExecutionList } from "./workflow-execution-list";
import { WorkflowExecutionEditor } from "./workflow-execution/workflow-execution-editor";
import { WorkflowExecutions } from "./workflow-execution/workflow-execution";
import { WorkflowExecutionStatus } from "../utils/workflow-execution-list.util";

const svc = vi.hoisted(() => ({
  getWorkflowExecutionById: vi.fn(),
  getWorkflowExecutions: vi.fn(),
  triggerListener: vi.fn().mockResolvedValue({}),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));

const execution = (id: string, extra: Record<string, unknown> = {}) => ({
  id,
  status: WorkflowExecutionStatus.Completed,
  executionMode: 1,
  startedAt: "2023-01-01T00:00:00Z",
  finishedAt: "2023-01-01T00:00:05Z",
  ...extra,
});

beforeEach(() => vi.clearAllMocks());

describe("WorkflowExecutionList", () => {
  it("renders the loading skeleton", () => {
    const { container } = render(
      <WorkflowExecutionList executions={[]} isLoading />,
    );
    expect(container.querySelectorAll("[class*='skeleton'], .h-6").length).toBeGreaterThan(0);
  });

  it("renders the empty state", () => {
    render(<WorkflowExecutionList executions={[]} />);
    expect(screen.getByText("No executions found.")).toBeTruthy();
  });

  it("renders executions and handles selection", () => {
    const onSelect = vi.fn();
    render(
      <WorkflowExecutionList
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        executions={[execution("e1"), execution("e2", { status: WorkflowExecutionStatus.Running, executionMode: 0, finishedAt: null }) as any]}
        selectedExecutionId="e1"
        onSelectExecution={onSelect}
      />,
    );
    expect(screen.getByText(/Completed in/)).toBeTruthy();
    expect(screen.getByText(/Started/)).toBeTruthy();
    fireEvent.click(screen.getAllByText(/2023/)[0]);
    expect(onSelect).toHaveBeenCalled();
  });
});

describe("WorkflowExecutionEditor", () => {
  it("prompts to select an execution when there is no id", () => {
    renderWithProviders(<WorkflowExecutionEditor />);
    expect(screen.getByText("Select an execution to view details")).toBeTruthy();
  });

  it("renders the execution graph once data is fetched", async () => {
    svc.getWorkflowExecutionById.mockResolvedValue({
      data: {
        workflowSnapshot: {
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
        nodeExecutions: [
          { nodeId: "n1", status: 4, inputItemCount: 1, outputItemCount: 1 },
        ],
        items: [{ nodeId: "n1", itemIndex: 0, data: {} }],
        errorMessage: null,
      },
    });
    renderWithProviders(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowExecutionEditor execution={{ id: "e1", status: 4, executionMode: 1 } as any} />,
    );
    await waitFor(() =>
      expect(svc.getWorkflowExecutionById).toHaveBeenCalledWith({ executionId: "e1" }),
    );
    await waitFor(() => expect(screen.getByText("Status:")).toBeTruthy());
  });

  it("shows the execution error trigger when an error message is present", async () => {
    svc.getWorkflowExecutionById.mockResolvedValue({
      data: {
        workflowSnapshot: {
          nodes: [],
          edges: [],
        },
        nodeExecutions: [],
        items: [],
        errorMessage: "Node execution failed",
      },
    });
    renderWithProviders(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowExecutionEditor execution={{ id: "e1", status: 5, executionMode: 1 } as any} />,
    );

    await waitFor(() => expect(screen.getByText("Error")).toBeTruthy());
  });
});

describe("WorkflowExecutions", () => {
  it("lists executions for the routed workflow", async () => {
    svc.getWorkflowExecutions.mockResolvedValue({ data: [execution("e1")] });
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={["/workflow/w1"]}>
          <Routes>
            <Route path="/workflow/:id" element={<WorkflowExecutions />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
    await waitFor(() =>
      expect(svc.getWorkflowExecutions).toHaveBeenCalledWith({ workflowId: "w1" }),
    );
    await waitFor(() =>
      expect(screen.getByText("Select an execution to view details")).toBeTruthy(),
    );
  });
});
