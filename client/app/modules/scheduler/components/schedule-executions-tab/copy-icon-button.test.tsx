import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const toasts = vi.hoisted(() => ({ showSuccessToast: vi.fn(), showErrorToast: vi.fn() }));
vi.mock("@/hooks/use-toast", () => toasts);

import { CopyIconButton } from "./copy-icon-button";

describe("CopyIconButton", () => {
  const writeText = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
  });
  afterEach(() => vi.restoreAllMocks());

  it("copies the value and shows the success toast", async () => {
    writeText.mockResolvedValue(undefined);
    render(<CopyIconButton value="e1" label="Execution id" />);

    await userEvent.click(screen.getByRole("button", { name: "Copy execution id" }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith("e1"));
    expect(toasts.showSuccessToast).toHaveBeenCalledWith({ description: "Execution id copied to clipboard." });
  });

  it("shows an error toast when the clipboard refuses", async () => {
    writeText.mockRejectedValue(new Error("denied"));
    render(<CopyIconButton value="e1" label="Execution id" />);

    await userEvent.click(screen.getByRole("button", { name: "Copy execution id" }));

    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalled());
  });
});
