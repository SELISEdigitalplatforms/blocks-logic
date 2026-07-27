import React from "react";
import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { NodeInspector } from "./node-inspector";
import { LayoutWithTest } from "./layouts/layout-with-test";
import { Sheet } from "@/components/ui-kits/sheet/sheet";

vi.mock("../../services/workflow.service", () => ({
  workflowService: {
    triggerListener: vi.fn().mockResolvedValue({}),
    getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: {} }),
    stepExecute: vi.fn().mockResolvedValue({}),
    updateWorkflow: vi.fn().mockResolvedValue({}),
  },
}));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (extra: Record<string, unknown> = {}): any => ({
  id: "n1",
  name: "Alpha",
  type: "webhook",
  category: "trigger",
  version: "v1",
  position: { x: 0, y: 0 },
  parameters: {},
  data: {},
  ...extra,
});

beforeEach(() => vi.clearAllMocks());

describe("NodeInspector", () => {
  it("renders nothing without a selected node", () => {
    const { container } = renderWithProviders(<NodeInspector />);
    expect(container.textContent).toBe("");
  });

  it("renders the listener layout for a webhook node", () => {
    renderWithProviders(<NodeInspector />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        const sel = node();
        store.getState().addNode(sel);
        store.setState({
          selectedNode: sel,
          isConfigModalOpen: true,
          editorMode: "editor",
        });
      },
    });
    expect(screen.getByText("Alpha")).toBeTruthy();
    expect(screen.getByText(/Receive data from a webhook/)).toBeTruthy();
  });
});

describe("LayoutWithTest", () => {
  const schema = {
    type: "webhook",
    category: "trigger",
    version: "v1",
    parameters: [{ id: "a", key: "a", type: "text", label: "Field A" }],
    settings: [],
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  } as any;

  it("returns null without a schema", () => {
    const { container } = renderWithProviders(
      <Sheet open>
        <LayoutWithTest schema={null} />
      </Sheet>,
    );
    expect(container.textContent).toBe("");
  });

  it("renders the parameters form and the test panel", () => {
    renderWithProviders(
      <Sheet open>
        <LayoutWithTest schema={schema} />
      </Sheet>,
      {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        seedWorkflow: (store: any) => {
          const sel = node({ name: "Beta" });
          store.getState().addNode(sel);
          store.setState({ selectedNode: sel, editorMode: "editor" });
        },
      },
    );
    expect(screen.getByText("Field A")).toBeTruthy();
    expect(screen.getByText("Run Test")).toBeTruthy();
  });
});
