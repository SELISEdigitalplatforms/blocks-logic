import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "../../../../test-utils/test-providers/render";
import { WorkflowEditorControls } from "./workflow-editor-controls";
import { workflowService } from "../../services/workflow.service";

vi.mock("../../services/workflow.service", () => ({
  workflowService: {
    triggerListener: vi.fn().mockResolvedValue({ isSuccess: true }),
    getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: {} }),
    updateWorkflow: vi.fn().mockResolvedValue({ id: "wf123", isSuccess: true }),
  },
}));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const triggerNode = (id: string, name?: string): any => ({
  id,
  name: name || id,
  type: "webhook",
  category: "trigger",
  position: { x: 0, y: 0 },
  parameters: {},
  data: {},
});

beforeEach(() => vi.clearAllMocks());

describe("WorkflowEditorControls", () => {
  it("renders the control buttons and triggers view actions", async () => {
    const user = userEvent.setup();
    renderWithProviders(<WorkflowEditorControls />);
    // Fit View / Zoom in / Zoom out / Organize / Open Node Library
    const buttons = screen.getAllByRole("button");
    expect(buttons.length).toBeGreaterThanOrEqual(5);
    for (const b of buttons) {
      await user.click(b);
    }
  });

  it("hides organize/library controls in readonly mode", () => {
    renderWithProviders(<WorkflowEditorControls readonly />);
    const buttons = screen.getAllByRole("button");
    // readonly slices to the first three view controls
    expect(buttons).toHaveLength(3);
  });

  it("shows a single execute button when there is one trigger node and automatically saves workflow on click", async () => {
    const user = userEvent.setup();
    const node = triggerNode("t1");
    renderWithProviders(<WorkflowEditorControls />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        store.getState().setWorkflow({
          itemId: "wf123",
          name: "Test WF",
          nodes: [node],
          edges: [],
        });
      },
    });
    // now there is an execute-workflow button in addition to the view controls
    const buttons = screen.getAllByRole("button");
    expect(buttons.length).toBeGreaterThan(5);
    // click the execute button (last one)
    await user.click(buttons[buttons.length - 1]);

    await waitFor(() => {
      expect(workflowService.updateWorkflow).toHaveBeenCalledWith(
        expect.objectContaining({
          itemId: "wf123",
          nodes: [expect.objectContaining({ id: "t1" })],
          edges: [],
        }),
        expect.anything()
      );
      expect(workflowService.triggerListener).toHaveBeenCalledWith(
        expect.objectContaining({
          WorkflowId: "wf123",
          TriggerId: "t1",
          EnableListener: true,
        })
      );
    });
  });

  it("automatically saves workflow when executing a trigger from dropdown with multiple triggers", async () => {
    const user = userEvent.setup();
    const node1 = triggerNode("t1", "Trigger One");
    const node2 = triggerNode("t2", "Trigger Two");
    renderWithProviders(<WorkflowEditorControls />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        store.getState().setWorkflow({
          itemId: "wf123",
          name: "Test WF",
          nodes: [node1, node2],
          edges: [],
        });
      },
    });

    const buttons = screen.getAllByRole("button");
    const playDropdownBtn = buttons[buttons.length - 1];
    await user.click(playDropdownBtn);

    const triggerItem = await screen.findByRole("menuitem", { name: "Trigger Two" });
    await user.click(triggerItem);

    await waitFor(() => {
      expect(workflowService.updateWorkflow).toHaveBeenCalledWith(
        expect.objectContaining({
          itemId: "wf123",
          nodes: expect.arrayContaining([
            expect.objectContaining({ id: "t1" }),
            expect.objectContaining({ id: "t2" }),
          ]),
          edges: [],
        }),
        expect.anything()
      );
      expect(workflowService.triggerListener).toHaveBeenCalledWith(
        expect.objectContaining({
          WorkflowId: "wf123",
          TriggerId: "t2",
          EnableListener: true,
        })
      );
    });
  });
});

