import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { formatDate, parseDateString } from "@/lib/utils";
import { schedule } from "../../__mocks__/schedule-execution.mock";
import { localDate } from "../../utils/execution-date-range";
import { scheduleDate, ScheduleTimingFields } from "./schedule-timing-fields";

describe("scheduleDate", () => {
  it("formats UTC dates exactly as before and other zones by their local day", () => {
    expect(scheduleDate("2026-11-01T00:00:00Z", "UTC")).toBe(formatDate(parseDateString("2026-11-01T00:00:00Z")));
    expect(scheduleDate("2026-10-31T18:00:00.000Z", "Asia/Dhaka")).toBe(formatDate(localDate("2026-11-01")));
  });
});

describe("ScheduleTimingFields", () => {
  it("shows the fallbacks and a dash when nothing is set", () => {
    renderWithProviders(<ScheduleTimingFields schedule={schedule({ startDate: null, endDate: null })} />);
    expect(screen.getByText("Immediate (No start date)")).toBeTruthy();
    expect(screen.getByText("Indefinite (No end date)")).toBeTruthy();
    expect(screen.getByText("Next run")).toBeTruthy();
    expect(screen.getByText("—")).toBeTruthy();
  });

  it("shows zoned dates and the next run for a Dhaka schedule", () => {
    renderWithProviders(
      <ScheduleTimingFields
        schedule={schedule({
          timeZone: "Asia/Dhaka",
          startDate: "2026-10-31T18:00:00.000Z",
          endDate: "2026-11-30T18:00:00.000Z",
          nextRunAtUtc: "2026-11-01T03:00:00Z",
        })}
      />,
    );
    expect(screen.getByText(formatDate(localDate("2026-11-01")))).toBeTruthy();
    expect(screen.getByText(formatDate(localDate("2026-12-01")))).toBeTruthy();
    expect(screen.getByText("Nov 1, 2026, 09:00:00")).toBeTruthy();
  });
});
