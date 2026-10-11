import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";

const deleteFunction = vi.fn();
vi.mock("../../services/function.service", () => ({
  functionService: { deleteFunction: (...args: unknown[]) => deleteFunction(...args) },
}));
const toasts = vi.hoisted(() => ({ showErrorToast: vi.fn(), showSuccessToast: vi.fn(), showInfoToast: vi.fn() }));
vi.mock("@/hooks/use-toast", () => toasts);

import { DeleteFunctionDialog, readDeleteBlocked } from "./delete-function-dialog";

/** The server's 400 when runs have not finished / workflows use the function (FN-66). */
const blocked = (activeRuns: number, workflows: number) => ({
  errors: {
    invalid_request: "this function cannot be deleted yet: it has 1 run(s) that have not finished",
    deleteBlocked: "true",
    activeRuns: String(activeRuns),
    workflows: String(workflows),
  },
});

const open = (onDeleted = vi.fn()) => {
  renderWithProviders(
    <DeleteFunctionDialog open onOpenChange={vi.fn()} fn={{ id: "fn_1", name: "QA FN-66 hold" }} onDeleted={onDeleted} />,
  );
  fireEvent.change(screen.getByLabelText(/to confirm/), { target: { value: "QA FN-66 hold" } });
  return onDeleted;
};

describe("DeleteFunctionDialog (FN-66)", () => {
  beforeEach(() => {
    deleteFunction.mockReset();
    Object.values(toasts).forEach((t) => t.mockReset());
  });

  it("deletes a function that nothing holds", async () => {
    deleteFunction.mockResolvedValue({ isSuccess: true });
    const onDeleted = open();

    fireEvent.click(screen.getByRole("button", { name: "Delete function" }));

    await waitFor(() => expect(onDeleted).toHaveBeenCalled());
    expect(deleteFunction).toHaveBeenCalledWith("fn_1", false);
  });

  it("explains unfinished runs and deletes only on Delete anyway", async () => {
    deleteFunction.mockRejectedValueOnce(blocked(1, 0)).mockResolvedValueOnce({ isSuccess: true });
    const onDeleted = open();

    fireEvent.click(screen.getByRole("button", { name: "Delete function" }));

    const alert = await screen.findByTestId("delete-function-blocked");
    expect(alert.textContent).toContain("1 run has not finished");
    expect(alert.textContent).toContain("cancels");
    expect(onDeleted).not.toHaveBeenCalled();
    expect(toasts.showErrorToast).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId("delete-function-anyway"));
    await waitFor(() => expect(onDeleted).toHaveBeenCalled());
    expect(deleteFunction).toHaveBeenLastCalledWith("fn_1", true);
  });

  it("names workflows too when they use the function", async () => {
    deleteFunction.mockRejectedValueOnce(blocked(2, 3));
    open();

    fireEvent.click(screen.getByRole("button", { name: "Delete function" }));

    const alert = await screen.findByTestId("delete-function-blocked");
    expect(alert.textContent).toContain("2 runs have not finished");
    expect(alert.textContent).toContain("3 workflows use it");
  });

  it("still toasts any other error", async () => {
    deleteFunction.mockRejectedValueOnce({ errors: { invalid_request: "boom" } });
    open();

    fireEvent.click(screen.getByRole("button", { name: "Delete function" }));

    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalled());
    expect(screen.queryByTestId("delete-function-blocked")).toBeNull();
  });

  it("reads the refusal only when the server marks it", () => {
    expect(readDeleteBlocked({ invalid_request: "x" })).toBeNull();
    expect(readDeleteBlocked(blocked(4, 0).errors)).toMatchObject({ activeRuns: 4, workflows: 0 });
  });
});
