import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const getEventSettings = vi.fn();
const saveEventSettings = vi.fn();
const getQueueStatus = vi.fn();
vi.mock("../services/function.service", () => ({
  functionService: {
    getEventSettings: (...args: unknown[]) => getEventSettings(...args),
    saveEventSettings: (...args: unknown[]) => saveEventSettings(...args),
    getQueueStatus: (...args: unknown[]) => getQueueStatus(...args),
  },
}));

import { useGetEventSettings, useGetQueueStatus, useSaveEventSettings } from "./use-events";

const SETTINGS = {
  functionId: "fn_1",
  events: [{ type: "SendMail", payload: { to: "a@b.c" } }],
  triggerEvents: ["order.paid"],
};

const setup = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, wrapper };
};

describe("useSaveEventSettings", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // A server that keeps what it was sent, so the refetch after a save returns the new state.
    let stored = SETTINGS;
    getEventSettings.mockImplementation(async () => stored);
    saveEventSettings.mockImplementation(async (body: typeof SETTINGS) => {
      stored = body;
      return { isSuccess: true };
    });
  });

  it("a trigger-events save sends the latest events too", async () => {
    const { wrapper } = setup();
    const settings = renderHook(() => useGetEventSettings("fn_1"), { wrapper });
    await waitFor(() => expect(settings.result.current.data).toBeTruthy());
    const save = renderHook(() => useSaveEventSettings("fn_1"), { wrapper });

    await act(() => save.result.current.mutateAsync({ triggerEvents: ["a.b.c"] }));

    expect(saveEventSettings).toHaveBeenCalledWith({
      functionId: "fn_1",
      events: SETTINGS.events,
      triggerEvents: ["a.b.c"],
    });
  });

  it("the second save builds on the first, not on the stale load", async () => {
    const { wrapper } = setup();
    const settings = renderHook(() => useGetEventSettings("fn_1"), { wrapper });
    await waitFor(() => expect(settings.result.current.data).toBeTruthy());
    const save = renderHook(() => useSaveEventSettings("fn_1"), { wrapper });

    await act(() => save.result.current.mutateAsync({ events: [] }));
    await act(() => save.result.current.mutateAsync({ triggerEvents: ["x.y.z"] }));

    expect(saveEventSettings).toHaveBeenLastCalledWith({
      functionId: "fn_1",
      events: [],
      triggerEvents: ["x.y.z"],
    });
  });

  it("loads the settings first when none are cached", async () => {
    const { wrapper } = setup();
    const save = renderHook(() => useSaveEventSettings("fn_1"), { wrapper });

    await act(() => save.result.current.mutateAsync({ events: [] }));

    expect(getEventSettings).toHaveBeenCalledWith("fn_1");
    expect(saveEventSettings).toHaveBeenCalledWith({
      functionId: "fn_1",
      events: [],
      triggerEvents: ["order.paid"],
    });
  });

  it("a failed save leaves the cached settings alone and rejects", async () => {
    const { wrapper, client } = setup();
    const settings = renderHook(() => useGetEventSettings("fn_1"), { wrapper });
    await waitFor(() => expect(settings.result.current.data).toBeTruthy());
    saveEventSettings.mockRejectedValue({ errors: { invalid_request: "events[0].payload: bad" } });
    const save = renderHook(() => useSaveEventSettings("fn_1"), { wrapper });

    await expect(act(() => save.result.current.mutateAsync({ events: [] }))).rejects.toBeTruthy();

    expect(client.getQueryData(["functions", "events", "settings", "fn_1"])).toEqual(SETTINGS);
  });
});

describe("useGetQueueStatus", () => {
  it("does not load without a function id", () => {
    const { wrapper } = setup();
    renderHook(() => useGetQueueStatus(undefined), { wrapper });
    expect(getQueueStatus).not.toHaveBeenCalled();
  });
});
