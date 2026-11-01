import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { scheduleService } from "../../services/schedule.service";
import { PREVIEW_DEBOUNCE_MS, ScheduleNextRuns } from "./schedule-next-runs";

const preview = () => vi.spyOn(scheduleService, "previewScheduleRuns");

const advance = async (ms = PREVIEW_DEBOUNCE_MS) => {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
};

describe("ScheduleNextRuns", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.restoreAllMocks();
  });
  afterEach(() => vi.useRealTimers());

  it("asks for a valid cron and makes no request", async () => {
    const spy = preview();
    renderWithProviders(<ScheduleNextRuns cronExpression="not a cron" timeZone="UTC" />);
    await advance();
    expect(screen.getByText("Enter a valid cron to preview")).toBeTruthy();
    expect(spy).not.toHaveBeenCalled();
  });

  it("shows up to three runs in the chosen zone, with the zone and dates in the request", async () => {
    const spy = preview().mockResolvedValue({
      isSuccess: true,
      data: { occurrencesUtc: ["2026-11-01T03:00:00Z", "2026-11-02T03:00:00Z", "2026-11-03T03:00:00Z"] },
    });
    renderWithProviders(<ScheduleNextRuns cronExpression="0 9 * * *" timeZone="Asia/Dhaka" startDate="2026-11-01" />);

    await advance();
    expect(await screen.findByText("Nov 1, 2026, 09:00:00")).toBeTruthy();
    expect(screen.getByText("Nov 3, 2026, 09:00:00")).toBeTruthy();
    expect(screen.getByText("Next runs").parentElement?.getAttribute("aria-live")).toBe("polite");
    expect(spy).toHaveBeenCalledWith({
      cronExpression: "0 9 * * *",
      timeZone: "Asia/Dhaka",
      startDate: "2026-10-31T18:00:00.000Z",
      endDate: null,
      count: 3,
    });
  });

  it("sends at most one request per 400 ms of idle input (after the initial value)", async () => {
    const spy = preview().mockResolvedValue({ isSuccess: true, data: { occurrencesUtc: [] } });
    const { rerender } = renderWithProviders(<ScheduleNextRuns cronExpression="0 9 * * *" timeZone="UTC" />);
    await advance(100);
    rerender(<ScheduleNextRuns cronExpression="0 10 * * *" timeZone="UTC" />);
    await advance(100);
    rerender(<ScheduleNextRuns cronExpression="0 11 * * *" timeZone="UTC" />);
    await advance();

    // The mount value goes out at once; the edits typed within 400 ms collapse into the last one.
    expect(spy).toHaveBeenCalledTimes(2);
    expect(spy).toHaveBeenNthCalledWith(1, expect.objectContaining({ cronExpression: "0 9 * * *" }));
    expect(spy).toHaveBeenNthCalledWith(2, expect.objectContaining({ cronExpression: "0 11 * * *", timeZone: null }));
    expect(spy).not.toHaveBeenCalledWith(expect.objectContaining({ cronExpression: "0 10 * * *" }));
  });

  it("says when no run falls in the date window", async () => {
    preview().mockResolvedValue({ isSuccess: true, data: { occurrencesUtc: [] } });
    renderWithProviders(<ScheduleNextRuns cronExpression="0 9 * * *" timeZone="UTC" endDate="2026-01-01" />);
    await advance();
    expect(await screen.findByText("No upcoming run in this date window.")).toBeTruthy();
  });

  it("shows Calculating while the request runs", async () => {
    preview().mockReturnValue(new Promise(() => undefined));
    renderWithProviders(<ScheduleNextRuns cronExpression="0 9 * * *" timeZone="UTC" />);
    expect(screen.getByText("Calculating…")).toBeTruthy();
    await advance();
    expect(screen.getByText("Calculating…")).toBeTruthy();
  });

  it("hides the block when the request fails or is refused", async () => {
    preview().mockResolvedValue({ isSuccess: false, data: null, errors: { validation_failed: "x" } });
    const { container } = renderWithProviders(<ScheduleNextRuns cronExpression="0 9 * * *" timeZone="UTC" />);
    await advance();
    await act(async () => {
      await Promise.resolve();
    });
    expect(container.textContent).toBe("");
  });
});
