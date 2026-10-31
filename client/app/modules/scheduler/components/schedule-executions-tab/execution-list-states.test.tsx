import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { schedule } from "../../__mocks__/schedule-execution.mock";
import {
  emptyStateMessage,
  ExecutionListError,
  NoExecutionsYet,
  NoMatchingExecutions,
  ScheduleGone,
} from "./execution-list-states";

describe("emptyStateMessage", () => {
  it("shows the next run when known", () => {
    expect(emptyStateMessage(schedule({ nextRunAtUtc: "2026-10-26T10:05:00Z" }), "UTC")).toBe(
      "Next run Oct 26, 2026, 10:05:00 (UTC)",
    );
  });

  it("explains an inactive schedule", () => {
    expect(emptyStateMessage(schedule({ isActive: false }), "UTC")).toBe(
      "This schedule is inactive. Activate it to start running.",
    );
  });

  it("explains an active schedule with no upcoming run", () => {
    expect(emptyStateMessage(schedule({ nextRunAtUtc: null }), "UTC")).toBe("No upcoming run in the current date window.");
  });

  it("falls back when the next-run field is absent", () => {
    expect(emptyStateMessage(schedule(), "UTC")).toBe("Runs will appear here after the schedule fires.");
  });
});

describe("list states", () => {
  it("renders the empty state", () => {
    renderWithProviders(<NoExecutionsYet schedule={schedule()} tz="UTC" />);
    expect(screen.getByText("No executions yet")).toBeTruthy();
    expect(screen.getByText("Runs will appear here after the schedule fires.")).toBeTruthy();
  });

  it("renders the no-match state", () => {
    renderWithProviders(<NoMatchingExecutions />);
    expect(screen.getByText("No executions match this filter.")).toBeTruthy();
  });

  it("renders the error state with Retry", async () => {
    const onRetry = vi.fn();
    renderWithProviders(<ExecutionListError onRetry={onRetry} />);
    expect(screen.getByText("Couldn't load executions")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalled();
  });

  it("renders the schedule-gone state with a link to the list", () => {
    renderWithProviders(
      <MemoryRouter>
        <ScheduleGone />
      </MemoryRouter>,
    );
    expect(screen.getByText("This schedule no longer exists.")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Back to schedules" }).getAttribute("href")).toBe("/schedule");
  });
});
