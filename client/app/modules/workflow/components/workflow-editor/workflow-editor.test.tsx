import { fireEvent, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { WorkflowEditor } from "./workflow-editor";

vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: {
    triggerListener: vi.fn().mockResolvedValue({}),
    getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: {} }),
  },
}));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const webhookNode = (id: string, extra: Record<string, unknown> = {}): any => ({
  id,
  name: id,
  type: "webhook",
  category: "trigger",
  version: "v1",
  position: { x: 0, y: 0 },
  parameters: {},
  data: { hasHandleArrow: true },
  ...extra,
});

beforeEach(() => vi.clearAllMocks());

describe("WorkflowEditor", () => {
  it("shows the empty-state add button and opens the library", () => {
    let store: unknown;
    renderWithProviders(<WorkflowEditor />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
      },
    });
    fireEvent.click(screen.getByText("Add first step"));
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((store as any).getState().isPanelOpen).toBe(true);
  });

  it("renders a node and sets editor mode", () => {
    let store: unknown;
    renderWithProviders(<WorkflowEditor />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().addNode(webhookNode("n1"));
      },
    });
    expect(screen.queryByText("Add first step")).toBeNull();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((store as any).getState().editorMode).toBe("editor");
  });

  it("handles copy and paste keyboard shortcuts", () => {
    let store: unknown;
    renderWithProviders(<WorkflowEditor />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().addNode(webhookNode("n1", { selected: true }));
      },
    });
    fireEvent.keyDown(window, { key: "c", ctrlKey: true });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((store as any).getState().copiedNodes.length).toBe(1);
    fireEvent.keyDown(window, { key: "v", ctrlKey: true });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect(Object.keys((store as any).getState().nodesMap).length).toBe(2);
  });

  it("ignores shortcuts while typing in an input", () => {
    let store: unknown;
    const { container } = renderWithProviders(<WorkflowEditor />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().addNode(webhookNode("n1", { selected: true }));
      },
    });
    const input = document.createElement("input");
    container.appendChild(input);
    fireEvent.keyDown(input, { key: "c", ctrlKey: true });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((store as any).getState().copiedNodes.length).toBe(0);
  });
});
