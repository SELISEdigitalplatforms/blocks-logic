import { describe, expect, it } from "vitest";
import { format } from "date-fns";
import { parseDateString } from "@/lib/utils";
import {
  scheduleFormDefaultValues,
  scheduleFormSchema,
  timeZoneErrorFrom,
  toFormDate,
  toIsoOrNull,
  toPayloadDate,
  toPayloadTimeZone,
} from "./utils";

describe("form time zone mapping", () => {
  it("defaults new schedules to UTC and requires a zone", () => {
    expect(scheduleFormDefaultValues.timeZone).toBe("UTC");
    const result = scheduleFormSchema.safeParse({ ...scheduleFormDefaultValues, timeZone: "" });
    expect(result.success).toBe(false);
  });

  it("sends UTC as null and any other zone as its id", () => {
    expect(toPayloadTimeZone("UTC")).toBeNull();
    expect(toPayloadTimeZone("Asia/Dhaka")).toBe("Asia/Dhaka");
  });

  it("keeps today's exact date strings for UTC", () => {
    for (const value of ["2026-11-01", "2026-03-08", "", undefined]) {
      expect(toPayloadDate(value, "UTC")).toBe(toIsoOrNull(value));
    }
    expect(toPayloadDate("2026-11-01", "UTC")).toBe("2026-11-01T00:00:00.000Z");
  });

  it("sends local midnight of the day in another zone", () => {
    expect(toPayloadDate("2026-11-01", "Asia/Dhaka")).toBe("2026-10-31T18:00:00.000Z");
    expect(toPayloadDate("2026-11-01", "Asia/Kathmandu")).toBe("2026-10-31T18:15:00.000Z");
    expect(toPayloadDate("", "Asia/Dhaka")).toBeNull();
    expect(toPayloadDate(undefined, "Asia/Dhaka")).toBeNull();
  });

  it("reads stored dates back as the same calendar day", () => {
    const utcIso = "2026-11-01T00:00:00Z";
    expect(toFormDate(utcIso, "UTC")).toBe(format(parseDateString(utcIso), "yyyy-MM-dd"));
    expect(toFormDate("2026-10-31T18:00:00.000Z", "Asia/Dhaka")).toBe("2026-11-01");
    expect(toFormDate(null, "Asia/Dhaka")).toBe("");
    expect(toFormDate(undefined, "UTC")).toBe("");
  });

  it("picks out the server's time zone refusal only", () => {
    expect(timeZoneErrorFrom({ validation_failed: "Time zone is not recognised" })).toBe("Time zone is not recognised");
    expect(timeZoneErrorFrom({ validation_failed: "Cron expression is not valid" })).toBeNull();
    expect(timeZoneErrorFrom(null)).toBeNull();
  });
});
