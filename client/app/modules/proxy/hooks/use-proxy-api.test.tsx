import { beforeEach, describe, expect, it } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { makeHookWrapper } from "@/test-utils/test-providers/render";
import {
  useCreateProxy,
  useExportProxyExecutionCsv,
  useGetProxies,
  useGetProxyExecutions,
  useRevertProxyVersion,
  useSendProxyTestRequest,
} from "./use-proxy-api";
import { proxyService } from "../services";

describe("use-proxy-api hooks", () => {
  beforeEach(() => {
    proxyService.resetMockStore();
  });

  it("reads and mutates proxy data through React Query", async () => {
    const wrapper = makeHookWrapper();
    const list = renderHook(() => useGetProxies(), { wrapper });
    const create = renderHook(() => useCreateProxy(), { wrapper });

    await waitFor(() => expect(list.result.current.data).toHaveLength(3));

    await act(async () => {
      await create.result.current.mutateAsync({
        name: "Docs API",
        upstreamUrl: "https://api.example.com/docs",
        methods: ["GET"],
        headers: [],
        query: [],
      });
    });

    await waitFor(() => expect(list.result.current.data?.[0].name).toBe("Docs API"));
  });

  it("uses dedicated hooks for logs, revert, test, and CSV export", async () => {
    const wrapper = makeHookWrapper();
    const logs = renderHook(() => useGetProxyExecutions("p1", "client"), { wrapper });
    const revert = renderHook(() => useRevertProxyVersion(), { wrapper });
    const testRequest = renderHook(() => useSendProxyTestRequest(), { wrapper });
    const exportCsv = renderHook(() => useExportProxyExecutionCsv(), { wrapper });

    await waitFor(() => expect(logs.result.current.data?.[0].status).toBe(404));

    await expect(
      revert.result.current.mutateAsync({ proxyId: "p1", versionId: "v2" }),
    ).resolves.toMatchObject({ isSuccess: true });

    await expect(
      testRequest.result.current.mutateAsync({ proxyId: "p1", method: "POST" }),
    ).resolves.toMatchObject({ ok: true, status: 200 });

    await expect(
      exportCsv.result.current.mutateAsync({ proxyId: "p1", filter: "all" }),
    ).resolves.toMatchObject({ rowCount: 3 });
  });
});

