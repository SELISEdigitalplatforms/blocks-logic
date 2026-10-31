import { describe, expect, it } from "vitest";
import {
  addDays,
  fromDateRangeValue,
  isYmd,
  localDate,
  toDateRangePayload,
  toDateRangeValue,
  validRange,
  ymd,
} from "./execution-date-range";

describe("calendar helpers", () => {
  it("validates yyyy-MM-dd days", () => {
    expect(isYmd("2026-10-26")).toBe(true);
    expect(isYmd("2026-02-30")).toBe(false);
    expect(isYmd("2026-1-5")).toBe(false);
    expect(isYmd("")).toBe(false);
    expect(isYmd(null)).toBe(false);
  });

  it("adds days across month and year ends", () => {
    expect(addDays("2026-10-26", 1)).toBe("2026-10-27");
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
  });

  it("round-trips a local calendar day", () => {
    const date = localDate("2026-03-08");
    expect(date.getFullYear()).toBe(2026);
    expect(date.getMonth()).toBe(2);
    expect(date.getDate()).toBe(8);
    expect(ymd(date)).toBe("2026-03-08");
  });
});

describe("validRange", () => {
  it("treats a missing to as the same day", () => {
    expect(validRange("2026-10-26", "")).toEqual({ from: "2026-10-26", to: "2026-10-26" });
  });

  it("drops malformed and reversed ranges", () => {
    expect(validRange("", "")).toBeNull();
    expect(validRange("26-10-2026", "")).toBeNull();
    expect(validRange("2026-10-26", "nope")).toBeNull();
    expect(validRange("2026-10-26", "2026-10-20")).toBeNull();
  });
});

describe("toDateRangePayload", () => {
  it("bounds Dhaka days at Dhaka midnights, end exclusive", () => {
    expect(toDateRangePayload("2026-10-20", "2026-10-26", "Asia/Dhaka")).toEqual({
      startDate: "2026-10-19T18:00:00.000Z",
      endDate: "2026-10-26T18:00:00.000Z",
    });
  });

  it("covers one day when only from is set", () => {
    expect(toDateRangePayload("2026-10-26", "", "Asia/Dhaka")).toEqual({
      startDate: "2026-10-25T18:00:00.000Z",
      endDate: "2026-10-26T18:00:00.000Z",
    });
    expect(toDateRangePayload("2026-10-26", "", "UTC")).toEqual({
      startDate: "2026-10-26T00:00:00.000Z",
      endDate: "2026-10-27T00:00:00.000Z",
    });
  });

  it("spans a DST change day at its real local midnights (23 hours)", () => {
    const { startDate, endDate } = toDateRangePayload("2026-03-08", "", "America/New_York");
    expect(startDate).toBe("2026-03-08T05:00:00.000Z");
    expect(endDate).toBe("2026-03-09T04:00:00.000Z");
  });

  it("sends no dates for a bad URL range", () => {
    expect(toDateRangePayload("bad", "", "UTC")).toEqual({ startDate: null, endDate: null });
    expect(toDateRangePayload("2026-10-26", "2026-10-01", "UTC")).toEqual({ startDate: null, endDate: null });
  });
});

describe("DateRange control mapping", () => {
  it("maps URL days to local dates", () => {
    const value = toDateRangeValue("2026-10-20", "2026-10-26");
    expect(value?.from && ymd(value.from)).toBe("2026-10-20");
    expect(value?.to && ymd(value.to)).toBe("2026-10-26");
    expect(toDateRangeValue("2026-10-20", "")).toEqual({ from: localDate("2026-10-20"), to: undefined });
    expect(toDateRangeValue("", "")).toBeNull();
    expect(toDateRangeValue("2026-10-26", "2026-10-01")).toBeNull();
  });

  it("maps an applied value back to URL days, and null clears", () => {
    expect(fromDateRangeValue({ from: localDate("2026-10-20"), to: localDate("2026-10-26") })).toEqual({
      from: "2026-10-20",
      to: "2026-10-26",
    });
    expect(fromDateRangeValue({ from: localDate("2026-10-20") })).toEqual({ from: "2026-10-20", to: "" });
    expect(fromDateRangeValue(null)).toEqual({ from: "", to: "" });
    expect(fromDateRangeValue({})).toEqual({ from: "", to: "" });
  });
});
