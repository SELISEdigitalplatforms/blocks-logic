import { serviceInstances } from "@/lib/http-client";
import { PROXY_ENDPOINTS, PROXY_MOCK_DATA, PROXY_MOCK_EXECUTION_LOGS, PROXY_MOCK_VERSION_HISTORY } from "../constants";
import { mapProxyToCreatePayload, mapProxyToUpdatePayload } from "../mappers";
import { compactKeyValues, maskUpstreamUrl, slugifyProxyName } from "../utils";
import {
  Proxy,
  ProxyCsvExport,
  ProxyExecutionLog,
  ProxyFormValues,
  ProxyListParams,
  ProxyLogFilter,
  ProxyMutationResponse,
  ProxyTestRequest,
  ProxyTestResponse,
  ProxyVersionHistory,
} from "../types";

let proxyStore: Proxy[] = PROXY_MOCK_DATA.map((proxy) => ({ ...proxy }));
let proxyLogs: ProxyExecutionLog[] = PROXY_MOCK_EXECUTION_LOGS.map((log) => ({ ...log }));
let proxyVersions: ProxyVersionHistory[] = PROXY_MOCK_VERSION_HISTORY.map((version) => ({ ...version }));

const waitForMock = async () => new Promise((resolve) => setTimeout(resolve, 10));

const buildProxy = (values: ProxyFormValues, existing?: Proxy): Proxy => {
  const now = new Date().toISOString();
  const upstreamUrl = values.upstreamUrl.trim();
  return {
    id: existing?.id ?? `p-${Date.now()}`,
    name: values.name.trim(),
    slug: slugifyProxyName(values.name),
    upstreamUrl,
    upstreamMasked: maskUpstreamUrl(upstreamUrl),
    methods: values.methods,
    enabled: existing?.enabled ?? true,
    headers: compactKeyValues(values.headers),
    query: compactKeyValues(values.query),
    calls24h: existing?.calls24h ?? 0,
    createdAt: existing?.createdAt ?? now,
    updatedAt: now,
  };
};

const matchesLogFilter = (status: number, filter: ProxyLogFilter) => {
  if (filter === "ok") return status >= 200 && status < 300;
  if (filter === "client") return status >= 400 && status < 500;
  if (filter === "server") return status >= 500 && status < 600;
  return true;
};

const toCsv = (rows: ProxyExecutionLog[]) => {
  const headers = ["TIME", "METH", "PATH", "CODE", "TOOK", "UPSTREAM"];
  const values = rows.map((row) =>
    [row.timeUtc, row.method, row.path, row.status, row.latencyMs, row.upstreamUrl]
      .map((value) => `"${String(value).replace(/"/g, '""')}"`)
      .join(","),
  );
  return [headers.join(","), ...values].join("\n");
};

export class ProxyService {
  private readonly logicHttpClient = serviceInstances.logicService;

  endpoints = PROXY_ENDPOINTS;

  getAll = async (params: ProxyListParams = {}): Promise<Proxy[]> => {
    void this.logicHttpClient;
    await waitForMock();
    const search = params.searchKey?.trim().toLowerCase();
    if (!search) return proxyStore;
    return proxyStore.filter((proxy) =>
      [proxy.name, proxy.slug, proxy.upstreamMasked].some((value) =>
        value.toLowerCase().includes(search),
      ),
    );
  };

  get = async (id: string): Promise<Proxy | null> => {
    await waitForMock();
    return proxyStore.find((proxy) => proxy.id === id) ?? null;
  };

  create = async (values: ProxyFormValues): Promise<ProxyMutationResponse> => {
    await waitForMock();
    const proxy = buildProxy(values);
    void mapProxyToCreatePayload(values);
    proxyStore = [proxy, ...proxyStore];
    return { isSuccess: true, itemId: proxy.id, data: proxy, errors: null };
  };

  update = async ({ id, values }: { id: string; values: ProxyFormValues }) => {
    await waitForMock();
    const existing = proxyStore.find((proxy) => proxy.id === id);
    if (!existing) return { isSuccess: false, errors: "Proxy not found" };
    const next = buildProxy(values, existing);
    void mapProxyToUpdatePayload(id, values);
    proxyStore = proxyStore.map((proxy) => (proxy.id === id ? next : proxy));
    return { isSuccess: true, itemId: id, data: next, errors: null };
  };

  toggle = async ({ id, enabled }: { id: string; enabled: boolean }) => {
    await waitForMock();
    const existing = proxyStore.find((proxy) => proxy.id === id);
    if (!existing) return { isSuccess: false, errors: "Proxy not found" };
    const next = { ...existing, enabled, updatedAt: new Date().toISOString() };
    proxyStore = proxyStore.map((proxy) => (proxy.id === id ? next : proxy));
    return { isSuccess: true, itemId: id, data: next, errors: null };
  };

  delete = async (id: string) => {
    await waitForMock();
    const exists = proxyStore.some((proxy) => proxy.id === id);
    if (!exists) return { isSuccess: false, errors: "Proxy not found" };
    proxyStore = proxyStore.filter((proxy) => proxy.id !== id);
    return { isSuccess: true, itemId: id, errors: null };
  };

  getExecutions = async (
    proxyId: string,
    filter: ProxyLogFilter,
    options: { live?: boolean } = {},
  ): Promise<ProxyExecutionLog[]> => {
    await waitForMock();
    const proxy = proxyStore.find((item) => item.id === proxyId);
    if (options.live && proxy?.enabled) {
      const liveLog: ProxyExecutionLog = {
        id: `live-${Date.now()}`,
        proxyId,
        timeUtc: new Date().toISOString(),
        method: proxy.methods[0] ?? "GET",
        path: `/api/proxy/gateway/${proxy.slug}/live`,
        status: 200,
        statusText: "OK",
        latencyMs: 120,
        upstreamHost: new URL(proxy.upstreamUrl).host,
        upstreamUrl: proxy.upstreamUrl,
        injectedHeaderKeys: proxy.headers.map((header) => header.key),
        injectedQueryKeys: proxy.query.map((query) => query.key),
        responseBody: "{\n  \"live\": true\n}",
        responseContentType: "application/json",
      };
      proxyLogs = [liveLog, ...proxyLogs].slice(0, 50);
    }
    return proxyLogs.filter((log) => log.proxyId === proxyId && matchesLogFilter(log.status, filter));
  };

  getVersions = async (proxyId: string): Promise<ProxyVersionHistory[]> => {
    await waitForMock();
    return proxyVersions.filter((version) => version.proxyId === proxyId);
  };

  revert = async ({ proxyId, versionId }: { proxyId: string; versionId: string }) => {
    await waitForMock();
    const version = proxyVersions.find((item) => item.proxyId === proxyId && item.id === versionId);
    const proxy = proxyStore.find((item) => item.id === proxyId);
    if (!version || !proxy || version.kind === "delete") {
      return { isSuccess: false, errors: "Unable to revert this proxy version." };
    }
    const next = { ...proxy, updatedAt: new Date().toISOString() };
    proxyStore = proxyStore.map((item) => (item.id === proxyId ? next : item));
    proxyVersions = [
      {
        id: `rv-${Date.now()}`,
        proxyId,
        versionLabel: `v${version.versionNumber + 1}`,
        versionNumber: version.versionNumber + 1,
        kind: "revert",
        summary: `Reverted to ${version.versionLabel}.`,
        actor: "Current user",
        whenUtc: new Date().toISOString(),
        before: version.after ?? null,
        after: version.before ?? null,
      },
      ...proxyVersions,
    ];
    return { isSuccess: true, itemId: proxyId, data: next, errors: null };
  };

  test = async (request: ProxyTestRequest): Promise<ProxyTestResponse> => {
    await waitForMock();
    const saved = request.proxyId ? proxyStore.find((proxy) => proxy.id === request.proxyId) : null;
    const upstream = request.draft?.upstreamUrl ?? saved?.upstreamUrl ?? "";
    if (!upstream.startsWith("https://")) {
      return {
        ok: false,
        status: 502,
        statusText: "Bad Gateway",
        latencyMs: 18,
        meta: "Mock proxy test rejected an invalid upstream endpoint.",
        responseBody: "{\n  \"error\": \"Invalid upstream. Use https:// endpoints only.\"\n}",
      };
    }
    return {
      ok: true,
      status: 200,
      statusText: "OK",
      latencyMs: 142,
      meta: `${request.method} ${request.pathSuffix || "/"} via mock proxy`,
      responseBody: "{\n  \"ok\": true,\n  \"source\": \"mock-proxy-test\"\n}",
    };
  };

  exportExecutionsCsv = async ({
    proxyId,
    filter,
  }: {
    proxyId: string;
    filter: ProxyLogFilter;
  }): Promise<ProxyCsvExport> => {
    await waitForMock();
    const proxy = proxyStore.find((item) => item.id === proxyId);
    if (!proxy) throw new Error("Proxy not found");
    const rows = proxyLogs.filter((log) => log.proxyId === proxyId && matchesLogFilter(log.status, filter));
    return {
      fileName: `proxy-${proxy.slug}-executions.csv`,
      csv: toCsv(rows),
      rowCount: rows.length,
    };
  };

  resetMockStore = () => {
    proxyStore = PROXY_MOCK_DATA.map((proxy) => ({ ...proxy }));
    proxyLogs = PROXY_MOCK_EXECUTION_LOGS.map((log) => ({ ...log }));
    proxyVersions = PROXY_MOCK_VERSION_HISTORY.map((version) => ({ ...version }));
  };
}

export const proxyService = new ProxyService();
