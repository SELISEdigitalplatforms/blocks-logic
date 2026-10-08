import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { makeHookWrapper } from "@/test-utils/test-providers/render";

const getRuns = vi.fn();
const testFunction = vi.fn();
vi.mock("../services/function.service", () => ({
  functionService: {
    getRuns: (...args: unknown[]) => getRuns(...args),
    testFunction: (...args: unknown[]) => testFunction(...args),
  },
}));

import { useGetRuns, useTestFunction } from "./use-runs";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const payload: any = { functionId: "fn_1", pageNumber: 0, pageSize: 20, fromUtc: "2026-10-08T00:00:00Z" };

const renderListAndTest = (autoRefresh: boolean) =>
  renderHook(
    () => ({ list: useGetRuns(payload, { autoRefresh }), test: useTestFunction() }),
    { wrapper: makeHookWrapper() },
  );

describe("runs list reloads (FN-59)", () => {
  beforeEach(() => {
    getRuns.mockReset().mockResolvedValue({ data: [], totalCount: 0 });
    testFunction.mockReset().mockResolvedValue({ runId: "r1" });
  });

  it("a finished test run does not reload a list whose auto-refresh is off", async () => {
    const hook = renderListAndTest(false);
    await waitFor(() => expect(getRuns).toHaveBeenCalledTimes(1));

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await act(() => hook.result.current.test.mutateAsync({ functionId: "fn_1" } as any));
    await new Promise((r) => setTimeout(r, 50));

    expect(getRuns).toHaveBeenCalledTimes(1);
  });

  it("a finished test run still reloads a live list", async () => {
    const hook = renderListAndTest(true);
    await waitFor(() => expect(getRuns).toHaveBeenCalledTimes(1));

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await act(() => hook.result.current.test.mutateAsync({ functionId: "fn_1" } as any));

    await waitFor(() => expect(getRuns).toHaveBeenCalledTimes(2));
  });
});
