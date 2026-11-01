import { describe, expect, it } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { NuqsTestingAdapter } from "nuqs/adapters/testing";
import { ReactNode } from "react";
import { useScheduleExecutionQueryParams } from "./use-schedule-execution-query-params";

const wrapper = (searchParams = "") => {
  const Wrapper = ({ children }: { children: ReactNode }) => (
    <NuqsTestingAdapter searchParams={searchParams} hasMemory>
      {children}
    </NuqsTestingAdapter>
  );
  return Wrapper;
};

describe("useScheduleExecutionQueryParams", () => {
  it("defaults to all outcomes, no dates, page 0 of 10", () => {
    const { result } = renderHook(() => useScheduleExecutionQueryParams(), { wrapper: wrapper() });
    expect(result.current.queryParams).toEqual({ outcome: "all", from: "", to: "", page: 0, pageSize: 10 });
  });

  it("reads the URL and updates it", async () => {
    const { result } = renderHook(() => useScheduleExecutionQueryParams(), {
      wrapper: wrapper("?outcome=Failed&from=2026-10-20&to=2026-10-26&page=3&pageSize=50"),
    });
    expect(result.current.queryParams).toEqual({ outcome: "Failed", from: "2026-10-20", to: "2026-10-26", page: 3, pageSize: 50 });

    await act(() => result.current.setQueryParams(null));
    expect(result.current.queryParams.page).toBe(0);
    expect(result.current.queryParams.outcome).toBe("all");
  });
});
