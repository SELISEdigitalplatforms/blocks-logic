import React from "react";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { InputPanel } from "./shared/input-panel/input-panel";
import { TestEventPanel } from "./shared/test-event-panel";
import { ListenEventPanel } from "./shared/listen-event-panel";
import { NodeInspectorHeader } from "./node-inspector-header";
import { LayoutWithIO } from "./layouts/layout-with-io";
import { Sheet } from "@/components/ui-kits/sheet/sheet";

// The inspector header/layout render Sheet primitives, so they must live inside
// a Sheet root in tests.
const inSheet = (ui: React.ReactElement) => <Sheet open>{ui}</Sheet>;

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => toasts);
vi.mock("../../services/workflow.service", () => ({
  workflowService: {
    triggerListener: vi.fn().mockResolvedValue({}),
    stepExecute: vi.fn().mockResolvedValue({}),
    updateWorkflow: vi.fn().mockResolvedValue({}),
    getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: {} }),
  },
}));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (id: string, extra: Record<string, unknown> = {}): any => ({
  id,
  name: id,
  type: "action",
  category: "action",
  version: "v1",
  position: { x: 0, y: 0 },
  parameters: {},
  data: {},
  ...extra,
});

beforeEach(() => vi.clearAllMocks());

describe("InputPanel", () => {
  it("returns null without a selected node", () => {
    const { container } = renderWithProviders(<InputPanel />);
    expect(container.textContent).toBe("");
  });

  it("shows input from a predecessor and switches tabs", async () => {
    const user = userEvent.setup();
    renderWithProviders(<InputPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        const parent = node("p");
        const sel = node("n1");
        store.getState().addNode(parent);
        store.getState().addNode(sel);
        store.getState().createEdge(
          { source: "p", sourceHandle: "s" },
          { target: "n1", targetHandle: "t" },
        );
        store.setState({
          selectedNode: sel,
          editorMode: "editor",
          executedNodes: [{ nodeId: "p", output: [{ name: "Ada" }] }],
        });
      },
    });
    expect(screen.getByText("Input")).toBeTruthy();
    await user.click(screen.getByRole("tab", { name: "Table" }));
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    await waitFor(() => expect(screen.getAllByText(/item 1/).length).toBeGreaterThan(0));
  });
});

describe("TestEventPanel", () => {
  it("runs a test and shows a success message", async () => {
    const onTest = vi.fn().mockResolvedValue(undefined);
    renderWithProviders(
      <TestEventPanel nodeId="n1" nodeType="action" onTest={onTest} />,
    );
    fireEvent.click(screen.getByText("Run Test"));
    await waitFor(() =>
      expect(screen.getByText("Test completed successfully")).toBeTruthy(),
    );
  });

  it("shows an error message when the test throws", async () => {
    const onTest = vi.fn().mockRejectedValue(new Error("failed run"));
    renderWithProviders(
      <TestEventPanel nodeId="n1" nodeType="action" onTest={onTest} />,
    );
    fireEvent.click(screen.getByText("Run Test"));
    await waitFor(() => expect(screen.getByText("failed run")).toBeTruthy());
  });
});

describe("ListenEventPanel", () => {
  it("returns null without a selected node", () => {
    const { container } = renderWithProviders(<ListenEventPanel />);
    expect(container.textContent).toBe("");
  });

  it("renders the listen button and toggles listening", () => {
    renderWithProviders(<ListenEventPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        const sel = node("n1", { category: "trigger", type: "webhook" });
        store.getState().addNode(sel);
        store.setState({ selectedNode: sel });
      },
    });
    fireEvent.click(screen.getByText("Listen For Test Event"));
    expect(screen.getByText(/Receive data from a webhook/)).toBeTruthy();
  });
});

describe("NodeInspectorHeader", () => {
  it("renders the node name and renames it", () => {
    renderWithProviders(inSheet(<NodeInspectorHeader />), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        const sel = node("n1", { name: "Alpha" });
        store.getState().addNode(sel);
        store.setState({ selectedNode: sel, editorMode: "editor" });
      },
    });
    expect(screen.getByText("Alpha")).toBeTruthy();
    // click the rename pen (first icon button)
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[0]);
    const input = document.querySelector("input") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "Beta" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByText("Beta")).toBeTruthy();
  });

  it("closes the config modal", () => {
    let store: unknown;
    renderWithProviders(inSheet(<NodeInspectorHeader />), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        const sel = node("n1");
        s.getState().addNode(sel);
        s.setState({ selectedNode: sel, isConfigModalOpen: true, editorMode: "editor" });
      },
    });
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[buttons.length - 1]);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((store as any).getState().isConfigModalOpen).toBe(false);
  });
});

describe("LayoutWithIO", () => {
  it("returns null without a schema", () => {
    const { container } = renderWithProviders(inSheet(<LayoutWithIO schema={null} />));
    expect(container.textContent).toBe("");
  });

  it("renders parameters and settings tabs with the form builder", async () => {
    const user = userEvent.setup();
    const schema = {
      type: "action",
      category: "action",
      version: "v1",
      parameters: [
        { id: "a", key: "a", type: "text", label: "Field A" },
      ],
      settings: [
        { id: "s", key: "s", type: "switch", label: "Setting S" },
      ],
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any;
    renderWithProviders(inSheet(<LayoutWithIO schema={schema} />), {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        const sel = node("n1", { name: "Alpha" });
        store.getState().addNode(sel);
        store.setState({ selectedNode: sel, editorMode: "editor" });
      },
    });
    expect(screen.getByText("Field A")).toBeTruthy();
    await user.click(screen.getByRole("tab", { name: "Settings" }));
    await waitFor(() => expect(screen.getByText("Setting S")).toBeTruthy());
  });
});
