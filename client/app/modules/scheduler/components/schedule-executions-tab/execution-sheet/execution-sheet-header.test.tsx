import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Sheet, SheetContent } from "@/components/ui-kits/sheet/sheet";
import { execution } from "../../../__mocks__/schedule-execution.mock";
import { ExecutionSheetHeader } from "./execution-sheet-header";

const inSheet = (ui: React.ReactElement) =>
  renderWithProviders(
    <Sheet open>
      <SheetContent>{ui}</SheetContent>
    </Sheet>,
  );

describe("ExecutionSheetHeader", () => {
  it("shows the title, fired time, outcome and status", () => {
    inSheet(<ExecutionSheetHeader execution={execution()} statusLabel="HTTP 200" tz="UTC" />);
    expect(screen.getByText("Execution details")).toBeTruthy();
    expect(screen.getByText("Oct 26, 2026, 10:05:00 (UTC)")).toBeTruthy();
    expect(screen.getByText("Succeeded")).toBeTruthy();
    expect(screen.getByText("HTTP 200")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /copy json/i })).toBeNull();
  });

  it("says when the fired time is unknown", () => {
    inSheet(<ExecutionSheetHeader execution={execution({ firedUtc: null })} statusLabel="—" tz="UTC" />);
    expect(screen.getByText("Fired time unknown (UTC)")).toBeTruthy();
  });

  it("shows a skeleton while loading", () => {
    inSheet(<ExecutionSheetHeader execution={null} statusLabel="" tz="UTC" loading />);
    expect(document.body.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.getByText("Loading execution")).toBeTruthy();
  });

  it("shows only the title when there is nothing to show", () => {
    inSheet(<ExecutionSheetHeader execution={null} statusLabel="" tz="UTC" />);
    expect(screen.getByText("Execution unavailable")).toBeTruthy();
  });
});
