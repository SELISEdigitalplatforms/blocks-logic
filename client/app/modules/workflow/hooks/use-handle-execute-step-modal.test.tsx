import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { useHandleExecuteStep } from "./use-handle-execute-step";

const svc = vi.hoisted(() => ({
  updateWorkflow: vi.fn().mockResolvedValue({}),
  stepExecute: vi.fn().mockResolvedValue({ code: "101" }),
  triggerListener: vi.fn().mockResolvedValue({}),
  getWorkflowExecutionById: vi.fn().mockResolvedValue({ data: {} }),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (id: string, category: string): any => ({
  id,
  name: id,
  type: category === "trigger" ? "webhook" : "action",
  category,
  position: { x: 0, y: 0 },
  parameters: {},
  data: {},
});

// A harness that renders the modal returned by the hook and exposes execute.
const Harness = () => {
  const { handleExecuteStep, executeStepModal } = useHandleExecuteStep();
  return (
    <div>
      <button onClick={() => handleExecuteStep("action")}>run</button>
      {executeStepModal}
    </div>
  );
};

beforeEach(() => vi.clearAllMocks());

describe("useHandleExecuteStep trigger selection modal", () => {
  it("lets the user pick a trigger when several precede the node", async () => {
    renderWithProviders(<Harness />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (store: any) => {
        const s = store.getState();
        s.setWorkflow({ itemId: "w1", name: "N" });
        s.addNode(node("action", "action"));
        s.addNode(node("t0", "trigger"));
        s.addNode(node("t1", "trigger"));
        s.createEdge({ source: "t0", sourceHandle: "m" }, { target: "action", targetHandle: "in" });
        s.createEdge({ source: "t1", sourceHandle: "m" }, { target: "action", targetHandle: "in" });
      },
    });
    fireEvent.click(screen.getByText("run"));
    // the selection modal lists the trigger nodes
    const trigger = await screen.findByText("t0");
    fireEvent.click(trigger);
    await waitFor(() =>
      expect(svc.triggerListener).toHaveBeenCalledWith(
        expect.objectContaining({ TriggerId: "t0" }),
      ),
    );
  });
});
