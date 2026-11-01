import { describe, expect, it } from "vitest";
import { isExecutionsFiltered, toExecutionsPayload } from "./execution-payload";

const params = { outcome: "all", from: "", to: "", page: 0, pageSize: 10 };

describe("toExecutionsPayload", () => {
  it("sends no filters by default", () => {
    const payload = toExecutionsPayload("s1", params, "UTC");
    expect(payload).toEqual({ scheduleId: "s1", outcome: null, startDate: null, endDate: null, pageNumber: 0, pageSize: 10 });
    expect(isExecutionsFiltered(payload)).toBe(false);
  });

  it("passes a known outcome and paging", () => {
    const payload = toExecutionsPayload("s1", { ...params, outcome: "Failed", page: 3, pageSize: 50 }, "UTC");
    expect(payload).toMatchObject({ outcome: "Failed", pageNumber: 3, pageSize: 50 });
    expect(isExecutionsFiltered(payload)).toBe(true);
  });

  it("drops an unknown outcome and a negative page", () => {
    expect(toExecutionsPayload("s1", { ...params, outcome: "Skipped", page: -2 }, "UTC")).toMatchObject({
      outcome: null,
      pageNumber: 0,
    });
  });

  it("clamps a hand-edited page size to what the API accepts", () => {
    expect(toExecutionsPayload("s1", { ...params, pageSize: 500 }, "UTC").pageSize).toBe(50);
    expect(toExecutionsPayload("s1", { ...params, pageSize: 0 }, "UTC").pageSize).toBe(1);
  });

  it("derives the date bounds in the schedule's zone", () => {
    const payload = toExecutionsPayload("s1", { ...params, from: "2026-10-26" }, "Asia/Dhaka");
    expect(payload).toMatchObject({ startDate: "2026-10-25T18:00:00.000Z", endDate: "2026-10-26T18:00:00.000Z" });
    expect(isExecutionsFiltered(payload)).toBe(true);
  });
});
