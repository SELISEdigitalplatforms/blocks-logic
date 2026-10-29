import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ExecutionSheetError, ExecutionSheetLoading, SnapshotUnavailable } from "./execution-sheet-states";

describe("execution sheet states", () => {
  it("shows the loading state", () => {
    render(<ExecutionSheetLoading />);
    expect(screen.getByRole("status").textContent).toContain("Loading execution…");
  });

  it("shows the error state with Retry", async () => {
    const onRetry = vi.fn();
    render(<ExecutionSheetError onRetry={onRetry} />);
    expect(screen.getByText("Couldn't load this execution.")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalled();
  });

  it("shows the not-found state without Retry", () => {
    render(<ExecutionSheetError notFound onRetry={vi.fn()} />);
    expect(screen.getByText("This execution no longer exists.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Retry" })).toBeNull();
  });

  it("shows the snapshot-unavailable note", () => {
    render(<SnapshotUnavailable />);
    expect(screen.getByText("Schedule snapshot unavailable for this run.")).toBeTruthy();
  });
});
