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

const SHIPPED = "order.shipped";
const SETTINGS = {
  functionId: "fn_1",
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
    getEventSettings.mockResolvedValue(SETTINGS);
  });

  it("a failed save leaves the cached settings alone and rejects", async () => {
    const { wrapper, client } = setup();
    const settings = renderHook(() => useGetEventSettings("fn_1"), { wrapper });
    await waitFor(() => expect(settings.result.current.data).toBeTruthy());
    saveEventSettings.mockRejectedValue({ errors: { invalid_request: "triggerEvents[0]: bad" } });
    const save = renderHook(() => useSaveEventSettings("fn_1"), { wrapper });

    await expect(act(() => save.result.current.mutateAsync({ triggerEvents: [] }))).rejects.toBeTruthy();

    expect(client.getQueryData(["functions", "events", "settings", "fn_1"])).toEqual(SETTINGS);
  });

  it("changing the action keeps the actions switch, and flipping the switch keeps the action", async () => {
    getEventSettings.mockResolvedValue({ ...SETTINGS, actionsEnabled: false });
    saveEventSettings.mockResolvedValue({ isSuccess: true });
    const { wrapper } = setup();
    const settings = renderHook(() => useGetEventSettings("fn_1"), { wrapper });
    await waitFor(() => expect(settings.result.current.data).toBeTruthy());
    const save = renderHook(() => useSaveEventSettings("fn_1"), { wrapper });
    // The reload after a save returns what the server now holds.
    getEventSettings.mockResolvedValue({ functionId: "fn_1", triggerEvents: [SHIPPED], actionsEnabled: false });

    await act(() => save.result.current.mutateAsync({ triggerEvents: [SHIPPED] }));
    expect(saveEventSettings).toHaveBeenLastCalledWith({
      functionId: "fn_1",
      triggerEvents: [SHIPPED],
      actionsEnabled: false,
    });

    await act(() => save.result.current.mutateAsync({ actionsEnabled: true }));
    expect(saveEventSettings).toHaveBeenLastCalledWith({
      functionId: "fn_1",
      triggerEvents: [SHIPPED],
      actionsEnabled: true,
    });
  });

  it("settings saved before the switch existed count as on", async () => {
    saveEventSettings.mockResolvedValue({ isSuccess: true });
    const { wrapper } = setup();
    const settings = renderHook(() => useGetEventSettings("fn_1"), { wrapper });
    await waitFor(() => expect(settings.result.current.data).toBeTruthy());
    const save = renderHook(() => useSaveEventSettings("fn_1"), { wrapper });

    await act(() => save.result.current.mutateAsync({ triggerEvents: [] }));

    expect(saveEventSettings).toHaveBeenLastCalledWith({
      functionId: "fn_1",
      triggerEvents: [],
      actionsEnabled: true,
    });
  });
});

describe("useGetQueueStatus", () => {
  it("does not load without a function id", () => {
    const { wrapper } = setup();
    renderHook(() => useGetQueueStatus(undefined), { wrapper });
    expect(getQueueStatus).not.toHaveBeenCalled();
  });
});
