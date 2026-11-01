import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { execution } from "../../../__mocks__/schedule-execution.mock";
import { ExecutionOverviewSection } from "./execution-overview-section";

describe("ExecutionOverviewSection", () => {
  it("lists the stored fields in the schedule's zone", () => {
    renderWithProviders(
      <ExecutionOverviewSection execution={execution()} statusLabel="HTTP 200" tz="Asia/Dhaka" currentName="Nightly sync" />,
    );
    // Fired and finished fall in the same second.
    expect(screen.getAllByText("Oct 26, 2026, 16:05:00")).toHaveLength(2);
    expect(screen.getByText("340 ms")).toBeTruthy();
    expect(screen.getByText("Nightly sync")).toBeTruthy();
    expect(screen.queryByText(/now:/)).toBeNull();
    expect(screen.queryByText("Execution id")).toBeNull();
    expect(screen.queryByRole("button", { name: "Copy execution id" })).toBeNull();
  });

  it("flags a rename since the run", () => {
    renderWithProviders(
      <ExecutionOverviewSection execution={execution()} statusLabel="HTTP 200" tz="UTC" currentName="Renamed sync" />,
    );
    expect(screen.getByText("(now: Renamed sync)")).toBeTruthy();
  });

  it("renders dashes for a partial row, never undefined or NaN", () => {
    const { container } = renderWithProviders(
      <ExecutionOverviewSection
        execution={execution({ firedUtc: null, finishedUtc: null, durationMs: null, scheduleName: null, status: null, outcome: "Failed" })}
        statusLabel="—"
        tz="UTC"
      />,
    );
    expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(4);
    expect(container.textContent).not.toMatch(/undefined|NaN|Invalid Date/);
    expect(screen.getByText("Failed")).toBeTruthy();
  });
});
