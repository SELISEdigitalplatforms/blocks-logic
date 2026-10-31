import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { Sheet, SheetContent } from "@/components/ui-kits/sheet/sheet";
import { execution } from "../../../__mocks__/schedule-execution.mock";

const toasts = vi.hoisted(() => ({ showSuccessToast: vi.fn(), showErrorToast: vi.fn() }));
vi.mock("@/hooks/use-toast", () => toasts);

import { ExecutionSheetHeader } from "./execution-sheet-header";

const inSheet = (ui: React.ReactElement) =>
  renderWithProviders(
    <Sheet open>
      <SheetContent>{ui}</SheetContent>
    </Sheet>,
  );

describe("ExecutionSheetHeader", () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  beforeEach(() => {
    vi.clearAllMocks();
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
  });

  it("shows the title, fired time, outcome and status", () => {
    inSheet(<ExecutionSheetHeader execution={execution()} statusLabel="HTTP 200" tz="UTC" />);
    expect(screen.getByText("Execution details")).toBeTruthy();
    expect(screen.getByText("Oct 26, 2026, 10:05:00 (UTC)")).toBeTruthy();
    expect(screen.getByText("Succeeded")).toBeTruthy();
    expect(screen.getByText("HTTP 200")).toBeTruthy();
  });

  it("says when the fired time is unknown", () => {
    inSheet(<ExecutionSheetHeader execution={execution({ firedUtc: null })} statusLabel="—" tz="UTC" />);
    expect(screen.getByText("Fired time unknown (UTC)")).toBeTruthy();
  });

  it("copies the full execution JSON", async () => {
    const row = execution();
    inSheet(<ExecutionSheetHeader execution={row} statusLabel="HTTP 200" tz="UTC" />);
    await userEvent.click(screen.getByRole("button", { name: /copy json/i }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(JSON.stringify(row, null, 2)));
    expect(toasts.showSuccessToast).toHaveBeenCalled();
  });

  it("reports a failed copy", async () => {
    writeText.mockRejectedValueOnce(new Error("denied"));
    inSheet(<ExecutionSheetHeader execution={execution()} statusLabel="HTTP 200" tz="UTC" />);
    await userEvent.click(screen.getByRole("button", { name: /copy json/i }));
    await waitFor(() => expect(toasts.showErrorToast).toHaveBeenCalled());
  });

  it("shows a skeleton while loading", () => {
    inSheet(<ExecutionSheetHeader execution={null} statusLabel="" tz="UTC" loading />);
    expect(document.body.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.getByText("Loading execution")).toBeTruthy();
  });

  it("shows only the title when there is nothing to show", () => {
    inSheet(<ExecutionSheetHeader execution={null} statusLabel="" tz="UTC" />);
    expect(screen.getByText("Execution unavailable")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /copy json/i })).toBeNull();
  });
});
