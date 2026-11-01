import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { ZonedTime } from "./zoned-time";

describe("ZonedTime", () => {
  it("renders the time in the zone inside <time dateTime>", () => {
    renderWithProviders(<ZonedTime iso="2026-10-26T10:05:00.120Z" tz="Asia/Dhaka" />);
    const time = screen.getByText("Oct 26, 2026, 16:05:00");
    expect(time.tagName).toBe("TIME");
    expect(time.getAttribute("datetime")).toBe("2026-10-26T10:05:00.120Z");
  });

  it("shows UTC and the viewer's time in a tooltip", async () => {
    renderWithProviders(<ZonedTime iso="2026-10-26T10:05:00.120Z" tz="Asia/Dhaka" />);
    await userEvent.hover(screen.getByText("Oct 26, 2026, 16:05:00"));
    expect((await screen.findAllByText(/UTC: Oct 26, 2026, 10:05:00 · Your time:/)).length).toBeGreaterThan(0);
  });

  it("renders a dash for a missing time", () => {
    renderWithProviders(<ZonedTime iso={null} tz="UTC" />);
    expect(screen.getByText("—")).toBeTruthy();
  });
});
