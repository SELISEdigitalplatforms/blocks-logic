import { fireEvent, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { WorkflowEditorControls } from "./workflow-editor-controls";

vi.mock("../../services/workflow.service", () => ({
  workflowService: {
    triggerListener: vi.fn().mockResolvedValue({}),
    getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: {} }),
  },
}));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const triggerNode = (id: string): any => ({
  id,
  name: id,
  type: "webhook",
  category: "trigger",
  position: { x: 0, y: 0 },
  parameters: {},
  data: {},
});

beforeEach(() => vi.clearAllMocks());

describe("WorkflowEditorControls", () => {
  it("renders the control buttons and triggers view actions", () => {
    renderWithProviders(<WorkflowEditorControls />);
    // Fit View / Zoom in / Zoom out / Organize / Open Node Library
    const buttons = screen.getAllByRole("button");
    expect(buttons.length).toBeGreaterThanOrEqual(5);
    buttons.forEach((b) => fireEvent.click(b));
  });

  it("hides organize/library controls in readonly mode", () => {
    renderWithProviders(<WorkflowEditorControls readonly />);
    const buttons = screen.getAllByRole("button");
    // readonly slices to the first three view controls
    expect(buttons.length).toBe(3);
  });

  it("shows a single execute button when there is one trigger node", () => {
    renderWithProviders(<WorkflowEditorControls />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => store.getState().addNode(triggerNode("t1")),
    });
    // now there is an execute-workflow button in addition to the view controls
    const buttons = screen.getAllByRole("button");
    expect(buttons.length).toBeGreaterThan(5);
    // click the execute button (last one)
    fireEvent.click(buttons[buttons.length - 1]);
  });
});
