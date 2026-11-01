import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { webhookSnapshot } from "../../../__mocks__/schedule-execution.mock";
import { parseSnapshot } from "../parse-snapshot";
import { ExecutionScheduleSection } from "./execution-schedule-section";

const snapshotOf = (raw: string) => {
  const parsed = parseSnapshot(raw);
  if (parsed.kind !== "ok") throw new Error("fixture must parse");
  return parsed.snapshot;
};

describe("ExecutionScheduleSection", () => {
  it("shows the cron, zone, window and description at fire time", () => {
    render(<ExecutionScheduleSection snapshot={snapshotOf(webhookSnapshot)} />);
    expect(screen.getByText("*/5 * * * *")).toBeTruthy();
    expect(screen.getByText("UTC")).toBeTruthy();
    expect(screen.getByText("Oct 1, 2026, 00:00:00 → No end")).toBeTruthy();
    expect(screen.getByText("Syncs every night")).toBeTruthy();
  });

  it("uses the snapshot's zone and placeholders for missing keys", () => {
    const { container } = render(
      <ExecutionScheduleSection snapshot={snapshotOf(JSON.stringify({ timeZone: "Asia/Dhaka" }))} />,
    );
    expect(screen.getByText("Asia/Dhaka")).toBeTruthy();
    expect(screen.getByText("No start → No end")).toBeTruthy();
    expect(screen.getByText("—")).toBeTruthy();
    expect(screen.queryByText("Description")).toBeNull();
    expect(container.textContent).not.toMatch(/undefined|NaN|Invalid Date/);
  });
});
