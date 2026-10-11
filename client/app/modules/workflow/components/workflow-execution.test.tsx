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
  resumeWorkflowExecution: vi.fn(),
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

  it("shows a footer while an older page is loading", () => {
    render(
      <WorkflowExecutionList
        executions={[execution("e1")]}
        hasMore
        isLoadingMore
        onLoadMore={vi.fn()}
      />,
    );
    expect(screen.getByText("Loading…")).toBeTruthy();
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

  const detail = (status: number) => ({
    data: { id: "e1", status, workflowSnapshot: { nodes: [], edges: [] }, nodeExecutions: [], items: [] },
  });

  it("offers Resume on a failed production run and calls the API", async () => {
    svc.getWorkflowExecutionById.mockResolvedValue(detail(WorkflowExecutionStatus.Failed));
    svc.resumeWorkflowExecution.mockResolvedValue({ isSuccess: true, executionId: "e1", resumedNodeIds: ["n2"] });
    renderWithProviders(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowExecutionEditor execution={{ id: "e1", status: WorkflowExecutionStatus.Failed, executionMode: 1 } as any} />,
    );
    fireEvent.click(await screen.findByTestId("execution-resume-button"));
    // React Query passes its own context as a second argument; the payload is the first.
    await waitFor(() => expect(svc.resumeWorkflowExecution.mock.calls[0]?.[0]).toEqual({ executionId: "e1" }));
  });

  it("says why when a run cannot be resumed", async () => {
    svc.getWorkflowExecutionById.mockResolvedValue(detail(WorkflowExecutionStatus.Failed));
    svc.resumeWorkflowExecution.mockResolvedValue({ isSuccess: false, executionId: "e1", resumedNodeIds: [], error: "already resumed" });
    renderWithProviders(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowExecutionEditor execution={{ id: "e1", status: WorkflowExecutionStatus.Failed, executionMode: 1 } as any} />,
    );
    fireEvent.click(await screen.findByTestId("execution-resume-button"));
    expect((await screen.findByRole("alert")).textContent).toContain("already resumed");
  });

  it.each([
    ["a completed run", WorkflowExecutionStatus.Completed, 1],
    ["a failed test run", WorkflowExecutionStatus.Failed, 0],
  ])("has no Resume on %s", async (_label, status, executionMode) => {
    svc.getWorkflowExecutionById.mockResolvedValue(detail(status));
    renderWithProviders(
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      <WorkflowExecutionEditor execution={{ id: "e1", status, executionMode } as any} />,
    );
    await waitFor(() => expect(screen.getByText("Status:")).toBeTruthy());
    expect(screen.queryByTestId("execution-resume-button")).toBeNull();
  });

  it("reads the status chip from the execution detail when the list row is still running", async () => {
    svc.getWorkflowExecutionById.mockResolvedValue({
      data: {
        id: "e1",
        status: WorkflowExecutionStatus.Completed,
        workflowSnapshot: { nodes: [], edges: [] },
        nodeExecutions: [],
        items: [],
      },
    });
    renderWithProviders(
      <WorkflowExecutionEditor
        execution={{
          id: "e1",
          status: WorkflowExecutionStatus.Running,
          executionMode: 1,
        } as never}
      />,
    );
    await waitFor(() => expect(screen.getByText("Completed")).toBeTruthy());
    expect(screen.queryByText("Running")).toBeNull();
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
      expect(svc.getWorkflowExecutions.mock.calls[0]?.[0]).toEqual({
        workflowId: "w1",
        pageSize: 20,
      }),
    );
    await waitFor(() =>
      expect(screen.getByText("Select an execution to view details")).toBeTruthy(),
    );
  });
});
