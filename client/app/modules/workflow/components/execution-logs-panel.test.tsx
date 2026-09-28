import React from "react";
import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import type { WorkflowStore } from "@/modules/workflow/store";
import { ExecutionLogsPanel } from "./workflow-execution/execution-logs-panel";
import { WorkflowExecutionEditor } from "./workflow-execution/workflow-execution-editor";
import { WorkflowExecutionStatus } from "../utils/workflow-execution-list.util";
import {
  ExecutionLogsAvailability,
  type ExecutionLogEntry,
  type IWorkflowExecutionLogs,
  type WorkflowExecution,
} from "../types/workflow.service.type";
import {
  DEFAULT_EXECUTION_LOG_FILTERS,
  NODE_FILTER_WORKFLOW,
  buildNodeFilterOptions,
  filterExecutionLogs,
  formatLogTime,
  formatLogsForCopy,
  nodeFilterValue,
} from "../utils/execution-logs.util";
import { executionLogsRefetchInterval } from "../hooks/use-workflow-api";

const svc = vi.hoisted(() => ({
  getWorkflowExecutionById: vi.fn(),
  getWorkflowExecutionLogs: vi.fn(),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));
// The inspector itself is covered elsewhere; here only its presence matters.
vi.mock("./node-inspector", () => ({
  NodeInspector: () => <div data-testid="node-inspector" />,
}));

const execution = {
  id: "e1",
  workflowId: "w1",
  status: WorkflowExecutionStatus.Completed,
  executionMode: 1,
  startedAt: "2026-09-01T10:00:00Z",
  finishedAt: "2026-09-01T10:00:05Z",
} as unknown as WorkflowExecution;

const entry = (overrides: Partial<ExecutionLogEntry>): ExecutionLogEntry => ({
  timestamp: "2026-09-01T10:00:00.000Z",
  level: "Information",
  stage: "node.input",
  message: "message",
  source: "worker",
  ...overrides,
});

const LOGS: ExecutionLogEntry[] = [
  entry({ stage: "execution.created", message: "Execution created. Mode Production, trigger webhook.", source: "api" }),
  entry({ stage: "node.started", nodeId: "n1", runIndex: 1, nodeName: "Incoming order", message: "Node 'Incoming order' started." }),
  entry({ stage: "node.noInput", level: "Warning", nodeId: "n2", runIndex: 2, nodeName: "Call CRM", message: "No input items; this branch was not taken." }),
  entry({ stage: "node.started", nodeId: "n2", runIndex: 3, nodeName: "Call CRM", message: "Node 'Call CRM' started." }),
  entry({ stage: "node.failed", level: "Error", nodeId: "n2", runIndex: 3, nodeName: "Call CRM", message: "Node failed after 412 ms (HttpRequestException)." }),
];

const response = (data: Partial<IWorkflowExecutionLogs>) => ({
  isSuccess: true,
  errors: null,
  data: {
    executionId: "e1",
    traceId: "0af7651916cd43dd8448eb211c80319c",
    availability: ExecutionLogsAvailability.Available,
    retentionDays: 30,
    mayStillArrive: false,
    isTruncated: false,
    logs: LOGS,
    ...data,
  },
});

const renderPanel = (open = true) =>
  renderWithProviders(
    <ExecutionLogsPanel execution={execution} nodeExecutions={[]} open={open} onOpenChange={vi.fn()} />,
  );

beforeEach(() => vi.clearAllMocks());

describe("execution logs utils", () => {
  it("filters by level, node and text, combined with AND", () => {
    const f = DEFAULT_EXECUTION_LOG_FILTERS;
    expect(filterExecutionLogs(LOGS, f)).toHaveLength(5);
    expect(filterExecutionLogs(LOGS, { ...f, levels: ["Error"] }).map((l) => l.stage)).toEqual(["node.failed"]);
    expect(filterExecutionLogs(LOGS, { ...f, node: NODE_FILTER_WORKFLOW }).map((l) => l.stage)).toEqual(["execution.created"]);
    expect(filterExecutionLogs(LOGS, { ...f, node: nodeFilterValue("n2") })).toHaveLength(3);
    expect(filterExecutionLogs(LOGS, { ...f, node: nodeFilterValue("n2", 3) })).toHaveLength(2);
    expect(filterExecutionLogs(LOGS, { ...f, search: "CALL crm" })).toHaveLength(3);
    expect(filterExecutionLogs(LOGS, { ...f, search: "node.failed" })).toHaveLength(1);
    expect(
      filterExecutionLogs(LOGS, { levels: ["Information"], node: nodeFilterValue("n2"), search: "started" }),
    ).toHaveLength(1);
  });

  it("builds node options with Workflow only and a per-run option for re-run nodes", () => {
    const options = buildNodeFilterOptions(LOGS, [{ nodeId: "n3", nodeName: "Never logged", runIndex: 4 }]);
    expect(options.map((o) => o.label)).toEqual([
      "All nodes",
      "Workflow only",
      "Incoming order",
      "Call CRM",
      "Call CRM · run 2",
      "Call CRM · run 3",
      "Never logged",
    ]);
    expect(options.find((o) => o.label === "Call CRM · run 3")!.value).toBe(nodeFilterValue("n2", 3));
  });

  it("copies lines as HH:mm:ss.SSS LEVEL [NodeName#run] message", () => {
    const time = formatLogTime(LOGS[0].timestamp);
    expect(time).toMatch(/^\d{2}:\d{2}:\d{2}\.\d{3}$/);
    expect(formatLogsForCopy([LOGS[0], LOGS[4]])).toBe(
      `${time} INFORMATION [Workflow] Execution created. Mode Production, trigger webhook.\n` +
        `${time} ERROR [Call CRM#3] Node failed after 412 ms (HttpRequestException).`,
    );
  });

  it("polls every 5 s only while logs may still arrive", () => {
    expect(executionLogsRefetchInterval(response({ mayStillArrive: true }))).toBe(5000);
    expect(executionLogsRefetchInterval(response({ mayStillArrive: false }))).toBe(false);
    expect(executionLogsRefetchInterval(undefined)).toBe(false);
  });
});

describe("ExecutionLogsPanel", () => {
  it("makes no request while closed", async () => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({}));
    renderPanel(false);
    await new Promise((r) => setTimeout(r, 20));
    expect(svc.getWorkflowExecutionLogs).not.toHaveBeenCalled();
  });

  it.each([
    [{ availability: ExecutionLogsAvailability.NotRecorded, logs: [] }, "It ran before execution logging was turned on."],
    [{ availability: ExecutionLogsAvailability.SourceUnavailable, logs: [] }, "Logs can't be loaded right now. Try again later."],
    [{ logs: [], mayStillArrive: true }, "Waiting for logs… They can take a few seconds to appear."],
    [{ logs: [], mayStillArrive: false }, "No logs were recorded for this execution."],
  ])("renders the %o state", async (data, text) => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(response(data));
    renderPanel();
    expect(await screen.findByText(text, { exact: false })).toBeTruthy();
  });

  it.each([
    [30, "Logs expire after 30 days."],
    [7, "Logs expire after 7 days."],
    [undefined, "Logs expire after 30 days."],
    [0, "Logs expire after 30 days."],
  ])("renders Expired with retentionDays=%s", async (retentionDays, text) => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(
      response({ availability: ExecutionLogsAvailability.Expired, logs: [], retentionDays: retentionDays as number }),
    );
    renderPanel();
    expect(await screen.findByText(text)).toBeTruthy();
    expect(screen.getByText(/This execution ran on/)).toBeTruthy();
  });

  it("shows a retryable error when the request fails", async () => {
    svc.getWorkflowExecutionLogs.mockRejectedValue(new Error("network"));
    renderPanel();
    expect(await screen.findByText("Couldn't load logs.")).toBeTruthy();
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({}));
    fireEvent.click(screen.getByText("Retry"));
    await waitFor(() => expect(screen.getAllByTestId("execution-log-row")).toHaveLength(5));
  });

  it("filters with level chips (at least one stays on) and clears filters", async () => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({}));
    renderPanel();
    await waitFor(() => expect(screen.getAllByTestId("execution-log-row")).toHaveLength(5));
    expect(screen.getByText("Showing 5 of 5 lines")).toBeTruthy();

    const info = screen.getByRole("button", { name: /^Info 3$/ });
    fireEvent.click(info);
    expect(info.getAttribute("aria-pressed")).toBe("false");
    expect(screen.getAllByTestId("execution-log-row")).toHaveLength(2);

    fireEvent.click(screen.getByRole("button", { name: /^Warning 1$/ }));
    fireEvent.click(screen.getByRole("button", { name: /^Error 1$/ }));
    // Error is the last one on, so it stays on.
    expect(screen.getByRole("button", { name: /^Error 1$/ }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getAllByTestId("execution-log-row")).toHaveLength(1);

    fireEvent.click(screen.getByText("Clear filters"));
    expect(screen.getAllByTestId("execution-log-row")).toHaveLength(5);
  });

  it("filters to a node when its chip is clicked", async () => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({}));
    renderPanel();
    await waitFor(() => expect(screen.getAllByTestId("execution-log-row")).toHaveLength(5));
    fireEvent.click(screen.getAllByTitle("Show only Call CRM")[0]);
    expect(screen.getAllByTestId("execution-log-row")).toHaveLength(3);
    expect(screen.getByText("Showing 3 of 5 lines")).toBeTruthy();
  });

  it("searches (debounced) and shows the no-match state", async () => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({}));
    renderPanel();
    await waitFor(() => expect(screen.getAllByTestId("execution-log-row")).toHaveLength(5));
    fireEvent.change(screen.getByPlaceholderText("Search logs"), { target: { value: "incoming" } });
    await waitFor(() => expect(screen.getAllByTestId("execution-log-row")).toHaveLength(1));
    fireEvent.change(screen.getByPlaceholderText("Search logs"), { target: { value: "zz-nothing" } });
    expect(await screen.findByText("No logs match these filters.")).toBeTruthy();
  });

  it("renders messages as text, never HTML", async () => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(
      response({ logs: [entry({ message: "Node '<b>bold</b>' started." })] }),
    );
    const { container } = renderPanel();
    expect(await screen.findByText("Node '<b>bold</b>' started.")).toBeTruthy();
    expect(container.ownerDocument.querySelector("[data-testid=execution-logs-panel] b")).toBeNull();
  });

  it("copies the filtered lines", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({}));
    renderPanel();
    await waitFor(() => expect(screen.getAllByTestId("execution-log-row")).toHaveLength(5));
    fireEvent.click(screen.getByRole("button", { name: /^Info 3$/ }));
    fireEvent.click(screen.getByRole("button", { name: /^Warning 1$/ }));
    fireEvent.click(screen.getByText("Copy logs"));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(formatLogsForCopy([LOGS[4]])));
  });

  it("shows the trace id and the truncation notice", async () => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({ isTruncated: true }));
    renderPanel();
    expect(await screen.findByText("Only the first 2,000 lines are shown.")).toBeTruthy();
    expect(screen.getByText("Trace 0af7651916cd43dd8448eb211c80319c")).toBeTruthy();
  });

  it("marks the list live while logs may still arrive", async () => {
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({ mayStillArrive: true }));
    renderPanel();
    await waitFor(() => expect(screen.getAllByTestId("execution-log-row")).toHaveLength(5));
    expect(screen.getByText("Live")).toBeTruthy();
    expect(screen.getByRole("log").getAttribute("aria-live")).toBe("polite");
  });
});

describe("WorkflowExecutionEditor execution logs button", () => {
  const renderEditor = () => {
    let store!: WorkflowStore;
    svc.getWorkflowExecutionById.mockResolvedValue({
      data: { workflowSnapshot: { nodes: [], edges: [] }, nodeExecutions: [], items: [] },
    });
    svc.getWorkflowExecutionLogs.mockResolvedValue(response({}));
    renderWithProviders(<WorkflowExecutionEditor execution={execution} />, {
      seedWorkflow: (s) => {
        store = s;
      },
    });
    return () => store;
  };

  const selectNode = (store: WorkflowStore) =>
    act(() => {
      store.getState().selectNode({
        id: "n1",
        name: "n1",
        type: "webhook",
        category: "trigger",
        version: "v1",
        position: { x: 0, y: 0 },
        parameters: {},
        data: {},
      } as never);
    });

  it("toggles the panel", async () => {
    renderEditor();
    const button = await screen.findByTestId("execution-logs-button");
    expect(button.getAttribute("aria-pressed")).toBe("false");
    expect(screen.queryByTestId("execution-logs-panel")).toBeNull();

    fireEvent.click(button);
    expect(await screen.findByTestId("execution-logs-panel")).toBeTruthy();
    expect(button.getAttribute("aria-pressed")).toBe("true");
    await waitFor(() => expect(svc.getWorkflowExecutionLogs).toHaveBeenCalledWith({ executionId: "e1" }));

    fireEvent.click(button);
    await waitFor(() => expect(screen.queryByTestId("execution-logs-panel")).toBeNull());
  });

  it("opening the panel deselects the selected node", async () => {
    const store = renderEditor();
    const button = await screen.findByTestId("execution-logs-button");
    selectNode(store());
    expect(store().getState().selectedNode).not.toBeNull();

    fireEvent.click(button);
    expect(store().getState().selectedNode).toBeNull();
    expect(await screen.findByTestId("execution-logs-panel")).toBeTruthy();
  });

  it("selecting a node while the panel is open closes it", async () => {
    const store = renderEditor();
    fireEvent.click(await screen.findByTestId("execution-logs-button"));
    expect(await screen.findByTestId("execution-logs-panel")).toBeTruthy();

    selectNode(store());
    await waitFor(() => expect(screen.queryByTestId("execution-logs-panel")).toBeNull());
    expect(screen.getByTestId("execution-logs-button").getAttribute("aria-pressed")).toBe("false");
  });
});
