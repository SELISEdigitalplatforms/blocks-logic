import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReactNode } from "react";
import { scheduleService } from "../services/schedule.service";
import { useGetScheduleExecution, useGetScheduleExecutions, usePreviewScheduleRuns } from "./use-schedule-api";

const makeWrapper = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const Wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return Wrapper;
};

const payload = { scheduleId: "s1", outcome: null, startDate: null, endDate: null, pageNumber: 0, pageSize: 10 };

describe("execution hooks", () => {
  beforeEach(() => vi.restoreAllMocks());
  afterEach(() => vi.useRealTimers());

  it("keeps polling across empty scheduler ticks until an execution appears", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const empty = { isSuccess: true, data: [], totalCount: 0 };
    const populated = { isSuccess: true, data: [{ id: "e1", outcome: "Succeeded" as const }], totalCount: 1 };
    const spy = vi.spyOn(scheduleService, "getScheduleExecutions")
      .mockResolvedValueOnce(empty)
      .mockResolvedValueOnce(empty)
      .mockResolvedValue(populated);
    const { result, unmount } = renderHook(() => useGetScheduleExecutions(payload), { wrapper: makeWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
    expect(spy).toHaveBeenCalledTimes(2);
    expect(result.current.data?.data).toEqual([]);
    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
    await waitFor(() => expect(result.current.data).toEqual(populated));
    expect(spy).toHaveBeenCalledTimes(3);
    unmount();
  });

  it("stops polling when the executions tab becomes inactive", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const spy = vi.spyOn(scheduleService, "getScheduleExecutions").mockResolvedValue({ isSuccess: true, data: [], totalCount: 0 });
    const { result, rerender, unmount } = renderHook(
      ({ enabled }) => useGetScheduleExecutions(payload, { enabled }),
      { wrapper: makeWrapper(), initialProps: { enabled: true } },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    rerender({ enabled: false });
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(spy).toHaveBeenCalledTimes(1);
    unmount();
  });

  it("lists executions when enabled", async () => {
    const spy = vi.spyOn(scheduleService, "getScheduleExecutions").mockResolvedValue({ isSuccess: true, data: [], totalCount: 0 });
    const { result } = renderHook(() => useGetScheduleExecutions(payload), { wrapper: makeWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(spy).toHaveBeenCalledWith(payload);
  });

  it("does not list while disabled", () => {
    const spy = vi.spyOn(scheduleService, "getScheduleExecutions");
    renderHook(() => useGetScheduleExecutions(payload, { enabled: false }), { wrapper: makeWrapper() });
    expect(spy).not.toHaveBeenCalled();
  });

  it("fetches one execution once, and never without an id", async () => {
    const spy = vi.spyOn(scheduleService, "getScheduleExecution").mockResolvedValue({ isSuccess: true, data: null });
    const wrapper = makeWrapper();
    const first = renderHook(() => useGetScheduleExecution("s1", "e1"), { wrapper });
    await waitFor(() => expect(first.result.current.isSuccess).toBe(true));
    // A second consumer of the same id reads the cache (staleTime: Infinity).
    const second = renderHook(() => useGetScheduleExecution("s1", "e1"), { wrapper });
    expect(second.result.current.isSuccess).toBe(true);
    renderHook(() => useGetScheduleExecution("s1", ""), { wrapper });
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith({ scheduleId: "s1", executionId: "e1" });
  });

  it("previews runs only when enabled", async () => {
    const spy = vi.spyOn(scheduleService, "previewScheduleRuns").mockResolvedValue({ isSuccess: true, data: { occurrencesUtc: [] } });
    const input = { cronExpression: "0 9 * * *", timeZone: null, count: 3 };
    const wrapper = makeWrapper();
    renderHook(() => usePreviewScheduleRuns(input, { enabled: false }), { wrapper });
    expect(spy).not.toHaveBeenCalled();

    const { result } = renderHook(() => usePreviewScheduleRuns(input), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(spy).toHaveBeenCalledWith(input);
  });
});
