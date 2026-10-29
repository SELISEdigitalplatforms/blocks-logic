import { afterEach, describe, expect, it, vi } from "vitest";
import { browserTimeZone, formatInZone, tzOffsetMs, zonedDateToUtcIso } from "./timezone";

const ISO = "2026-10-26T10:05:00.120Z";

describe("formatInZone", () => {
  it("formats a datetime in the given zone with a 24-hour clock", () => {
    expect(formatInZone(ISO, "UTC", "datetime")).toBe("Oct 26, 2026, 10:05:00");
    expect(formatInZone(ISO, "Asia/Dhaka", "datetime")).toBe("Oct 26, 2026, 16:05:00");
  });

  it("formats a calendar day and a time", () => {
    expect(formatInZone("2026-10-26T20:00:00Z", "Asia/Dhaka", "yyyy-MM-dd")).toBe("2026-10-27");
    expect(formatInZone(ISO, "UTC", "time")).toBe("10:05:00");
  });

  it("falls back to UTC for an unknown zone and to a dash for an invalid instant", () => {
    expect(formatInZone(ISO, "Not/AZone", "datetime")).toBe("Oct 26, 2026, 10:05:00");
    expect(formatInZone("not-a-date", "UTC", "datetime")).toBe("—");
  });
});

describe("tzOffsetMs", () => {
  it("is the zone's offset east of UTC", () => {
    expect(tzOffsetMs("UTC", new Date(ISO))).toBe(0);
    expect(tzOffsetMs("Asia/Dhaka", new Date(ISO))).toBe(6 * 3600_000);
    expect(tzOffsetMs("America/New_York", new Date("2026-01-15T12:00:00Z"))).toBe(-5 * 3600_000);
    expect(tzOffsetMs("America/New_York", new Date("2026-07-15T12:00:00Z"))).toBe(-4 * 3600_000);
  });
});

describe("zonedDateToUtcIso", () => {
  it("is local midnight of that day in the zone", () => {
    expect(zonedDateToUtcIso("2026-10-20", "Asia/Dhaka")).toBe("2026-10-19T18:00:00.000Z");
    expect(zonedDateToUtcIso("2026-10-26", "UTC")).toBe("2026-10-26T00:00:00.000Z");
  });

  it("uses the real midnight on both sides of a DST change", () => {
    // New York springs forward on 2026-03-08 at 02:00 local.
    expect(zonedDateToUtcIso("2026-03-08", "America/New_York")).toBe("2026-03-08T05:00:00.000Z");
    expect(zonedDateToUtcIso("2026-03-09", "America/New_York")).toBe("2026-03-09T04:00:00.000Z");
  });
});

describe("browserTimeZone", () => {
  afterEach(() => vi.restoreAllMocks());

  it("is the resolved zone, or UTC when the runtime reports none", () => {
    expect(browserTimeZone()).toBeTruthy();
    vi.spyOn(Intl.DateTimeFormat.prototype, "resolvedOptions").mockReturnValue({
      timeZone: "",
    } as Intl.ResolvedDateTimeFormatOptions);
    expect(browserTimeZone()).toBe("UTC");
  });
});
