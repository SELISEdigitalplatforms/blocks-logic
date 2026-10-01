import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { RunsTable } from "./runs-table";
import { IRunSummary } from "../../types/run.types";

vi.mock("../../services/function.service", () => ({
  functionService: { replayRun: vi.fn(), cancelRun: vi.fn() },
}));

const succeeded: IRunSummary = {
  id: "run_7f21c4",
  functionId: "fn_1",
  versionNumber: 17,
  status: "Succeeded",
  invokedBy: "Http",
  attempt: 1,
  createdDate: new Date(Date.now() - 3 * 60_000).toISOString(),
  durationMs: 684,
  peakMemoryBytes: 86_000_000,
};

const running: IRunSummary = {
  ...succeeded,
  id: "run_991aa2",
  status: "Running",
  invokedBy: "Workflow",
  durationMs: null,
  peakMemoryBytes: null,
};

describe("RunsTable", () => {
  it("shows duration, peak memory against the limit, trigger and version", () => {
    renderWithProviders(
      <RunsTable runs={[succeeded]} isLoading={false} memoryLimitMb={192} onOpenRun={vi.fn()} />,
    );

    expect(screen.getByText("run_7f21c4")).toBeTruthy();
    expect(screen.getByText("684 ms")).toBeTruthy();
    expect(screen.getByText("82 / 192 MB")).toBeTruthy();
    expect(screen.getByText("HTTP")).toBeTruthy();
    expect(screen.getByText("v17")).toBeTruthy();
    expect(screen.getByText("3 min ago")).toBeTruthy();
  });

  it("opens a run when its row is clicked", async () => {
    const onOpenRun = vi.fn();
    renderWithProviders(<RunsTable runs={[succeeded]} isLoading={false} onOpenRun={onOpenRun} />);

    await userEvent.click(screen.getByText("run_7f21c4"));

    expect(onOpenRun).toHaveBeenCalledWith("run_7f21c4");
  });

  it("offers Cancel on an active run and Replay on a finished one", async () => {
    const { unmount } = renderWithProviders(
      <RunsTable runs={[running]} isLoading={false} onOpenRun={vi.fn()} />,
    );
    await userEvent.click(screen.getByRole("button", { name: /actions for run run_991aa2/i }));
    expect(screen.getByRole("menuitem", { name: /cancel/i })).toBeTruthy();
    expect(screen.queryByRole("menuitem", { name: /replay/i })).toBeNull();
    unmount();

    renderWithProviders(<RunsTable runs={[succeeded]} isLoading={false} onOpenRun={vi.fn()} />);
    await userEvent.click(screen.getByRole("button", { name: /actions for run run_7f21c4/i }));
    expect(screen.getByRole("menuitem", { name: /replay/i })).toBeTruthy();
    expect(screen.queryByRole("menuitem", { name: /cancel/i })).toBeNull();
  });

  it("tells an undeployed function apart from a filtered-out page", () => {
    const { unmount } = renderWithProviders(
      <RunsTable runs={[]} isLoading={false} onOpenRun={vi.fn()} />,
    );
    expect(screen.getByText(/deploy the function and call its endpoint/i)).toBeTruthy();
    unmount();

    renderWithProviders(<RunsTable runs={[]} isLoading={false} hasFilters onOpenRun={vi.fn()} />);
    expect(screen.getByText(/no runs match this filter/i)).toBeTruthy();
  });

  it("states the retention period", () => {
    renderWithProviders(<RunsTable runs={[succeeded]} isLoading={false} onOpenRun={vi.fn()} />);
    expect(screen.getByText(/kept 30 days/i)).toBeTruthy();
  });
});
