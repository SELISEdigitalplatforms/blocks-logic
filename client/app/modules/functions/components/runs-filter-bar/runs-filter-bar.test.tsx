import { describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RunsFilterBar, RunsFilterValue } from "./runs-filter-bar";

const value: RunsFilterValue = {
  status: "",
  invokedBy: "",
  range: "7d",
  search: "",
  autoRefresh: true,
};

describe("RunsFilterBar", () => {
  it("offers the design's status chips", () => {
    renderWithProviders(<RunsFilterBar value={value} onChange={vi.fn()} hasActiveRun={false} />);

    ["All", "Succeeded", "Failed", "Timed out", "Running"].forEach((label) => {
      expect(screen.getByRole("button", { name: label })).toBeTruthy();
    });
  });

  it("reports a chosen status", async () => {
    const onChange = vi.fn();
    renderWithProviders(<RunsFilterBar value={value} onChange={onChange} hasActiveRun={false} />);

    await userEvent.click(screen.getByRole("button", { name: "Timed out" }));

    expect(onChange).toHaveBeenCalledWith({ status: "TimedOut" });
  });

  it("reports a run-id search once the debounce has elapsed", async () => {
    const onChange = vi.fn();
    renderWithProviders(<RunsFilterBar value={value} onChange={onChange} hasActiveRun={false} />);

    await userEvent.type(screen.getByLabelText(/search by run id/i), "r");

    // The shared SearchInput debounces, so the keystroke must not have reached the caller yet:
    // that is what keeps one request per character off the wire.
    expect(onChange).not.toHaveBeenCalled();

    await waitFor(() => expect(onChange).toHaveBeenCalledWith({ search: "r" }));
  });

  it("lets auto-refresh be turned off", async () => {
    const onChange = vi.fn();
    renderWithProviders(<RunsFilterBar value={value} onChange={onChange} hasActiveRun />);

    await userEvent.click(screen.getByRole("switch"));

    expect(onChange).toHaveBeenCalledWith({ autoRefresh: false });
  });
});
