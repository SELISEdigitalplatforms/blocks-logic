import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { DeleteWorkflow } from "./delete-workflow";

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => toasts);
const svc = vi.hoisted(() => ({
  deleteWorkflow: vi.fn(),
}));
vi.mock("@/modules/workflow/services/workflow.service", () => ({
  workflowService: svc,
}));

const renderDelete = (onDeleted?: () => void) => {
  const onOpenChange = vi.fn();
  renderWithProviders(
    <DeleteWorkflow workflowId="wf1" open onOpenChange={onOpenChange} onDeleted={onDeleted} />,
  );
  return { onOpenChange };
};

beforeEach(() => vi.clearAllMocks());

describe("DeleteWorkflow", () => {
  it("deletes, closes and calls onDeleted on success", async () => {
    svc.deleteWorkflow.mockResolvedValue({ isSuccess: true });
    const onDeleted = vi.fn();
    const { onOpenChange } = renderDelete(onDeleted);

    await userEvent.click(screen.getByRole("button", { name: "Yes" }));

    await waitFor(() => expect(onDeleted).toHaveBeenCalledTimes(1));
    expect(svc.deleteWorkflow.mock.calls[0][0]).toEqual({ id: "wf1" });
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Workflow deleted successfully" });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("does not call onDeleted when the server rejects the delete", async () => {
    svc.deleteWorkflow.mockResolvedValue({ isSuccess: false, errors: "Not allowed" });
    const onDeleted = vi.fn();
    const { onOpenChange } = renderDelete(onDeleted);

    await userEvent.click(screen.getByRole("button", { name: "Yes" }));

    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "Not allowed" }));
    expect(onDeleted).not.toHaveBeenCalled();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("does not call onDeleted when the request throws", async () => {
    svc.deleteWorkflow.mockRejectedValue(new Error("network"));
    const onDeleted = vi.fn();
    renderDelete(onDeleted);

    await userEvent.click(screen.getByRole("button", { name: "Yes" }));

    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }));
    expect(onDeleted).not.toHaveBeenCalled();
  });

  it("still deletes and closes without an onDeleted callback", async () => {
    svc.deleteWorkflow.mockResolvedValue({ isSuccess: true });
    const { onOpenChange } = renderDelete();

    await userEvent.click(screen.getByRole("button", { name: "Yes" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(toasts.showSuccessToast).toHaveBeenCalled();
  });
});
