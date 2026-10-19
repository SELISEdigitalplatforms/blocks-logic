import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CopyValueButton } from "./copy-value-button";

const clipboard = vi.hoisted(() => ({
  copyToClipboard: vi.fn(),
}));
vi.mock("@blocks-workflow/utils/copy-to-clipboard", () => clipboard);

const isShowingCheck = () =>
  screen.getByRole("button", { name: "Copy value" }).querySelector(".lucide-check") !== null;

describe("CopyValueButton", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("copies the value and shows a check, then reverts to the copy icon", async () => {
    clipboard.copyToClipboard.mockResolvedValue(true);
    render(<CopyValueButton value="secret" label="Copy value" />);

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Copy value" }));
    });

    expect(clipboard.copyToClipboard).toHaveBeenCalledWith("secret");
    expect(isShowingCheck()).toBe(true);

    act(() => {
      vi.advanceTimersByTime(1500);
    });
    expect(isShowingCheck()).toBe(false);
  });

  it("keeps the copy icon when copying fails", async () => {
    clipboard.copyToClipboard.mockResolvedValue(false);
    render(<CopyValueButton value="secret" label="Copy value" />);

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Copy value" }));
    });

    expect(isShowingCheck()).toBe(false);
  });
});
