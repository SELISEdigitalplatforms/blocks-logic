/**
 * In-memory stand-in for {@link ProxyService}, used by the hook / component tests that predate the
 * real HTTP wiring. It mirrors the shapes the live service returns so those tests can keep asserting
 * on rendered output without a running API. New tests should mock `@/lib/http-client` instead.
 */
import {
  PROXY_MOCK_DATA,
  PROXY_MOCK_EXECUTION_LOGS,
  PROXY_MOCK_VERSION_HISTORY,
} from "../constants";
import {
  Proxy,
  ProxyCsvExport,
  ProxyExecutionLog,
  ProxyExecutionPage,
  ProxyFieldChange,
  ProxyFormValues,
  ProxyListParams,
  ProxyLogFilter,
  ProxyMethod,
  ProxyMutationResponse,
  ProxyOverview,
  ProxyTestRequest,
  ProxyTestResponse,
  ProxyVersionHistory,
} from "../types";
import { compactKeyValues, containsVarRef, maskUpstreamUrl, slugifyProxyName } from "../utils";

let proxyStore: Proxy[] = PROXY_MOCK_DATA.map((proxy) => ({ ...proxy }));
let proxyLogs: ProxyExecutionLog[] = PROXY_MOCK_EXECUTION_LOGS.map((log) => ({ ...log }));
let proxyVersions: ProxyVersionHistory[] = PROXY_MOCK_VERSION_HISTORY.map((version) => ({
  ...version,
}));

const waitForMock = () => new Promise((resolve) => setTimeout(resolve, 10));

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
    // Mirror the mapper's tab gate: a "passthrough" save persists [] regardless of typed rows.
    bodyMerge:
      values.bodyMode === "passthrough" ? [] : compactKeyValues(values.bodyMerge ?? []),
    methodConfigs: (values.methodConfigs ?? [])
      .filter((entry) => values.methods.includes(entry.method))
      .map((entry) => {
        const headers = entry.headers ? compactKeyValues(entry.headers) : null;
        const query = entry.query ? compactKeyValues(entry.query) : null;
        return {
          method: entry.method,
          upstream: entry.upstream?.trim() || null,
          headers: headers && headers.length ? headers : null,
          query: query && query.length ? query : null,
        };
      })
      .filter((entry) => entry.upstream || entry.headers || entry.query),
    calls24h: existing?.calls24h ?? 0,
    createdAt: existing?.createdAt ?? now,
    updatedAt: now,
  };
};

const METHOD_TOKENS: ProxyMethod[] = ["GET", "POST", "PUT", "PATCH", "DELETE"];

const readProxyField = (proxy: Proxy, field: string): string | null => {
  if (field === "name") return proxy.name;
  if (field === "upstream") return proxy.upstreamUrl;
  if (field === "enabled") return proxy.enabled ? "enabled" : "disabled";
  if (field === "methods") return proxy.methods.join(", ");
  if (field.startsWith("header:"))
    return proxy.headers.find((h) => h.key === field.slice(7))?.value ?? null;
  if (field.startsWith("query:"))
    return proxy.query.find((q) => q.key === field.slice(6))?.value ?? null;
  if (field.startsWith("body:"))
    return proxy.bodyMerge.find((b) => b.key === field.slice(5))?.value ?? null;
  if (field.startsWith("method:")) {
    const [, method, ...rest] = field.split(":");
    const tail = rest.join(":");
    const entry = proxy.methodConfigs.find((c) => c.method === method);
    if (!entry) return null;
    if (tail === "upstream") return entry.upstream;
    if (tail.startsWith("header:"))
      return entry.headers?.find((h) => h.key === tail.slice(7))?.value ?? null;
    if (tail.startsWith("query:"))
      return entry.query?.find((q) => q.key === tail.slice(6))?.value ?? null;
  }
  return null;
};

const applyProxyField = (proxy: Proxy, field: string, value: string | null): Proxy => {
  if (field === "name") return { ...proxy, name: value ?? "" };
  if (field === "upstream") return { ...proxy, upstreamUrl: value ?? "" };
  if (field === "enabled") return { ...proxy, enabled: value === "enabled" };
  if (field === "methods") {
    const methods = (value ?? "")
      .split(",")
      .map((m) => m.trim().toUpperCase())
      .filter((m): m is ProxyMethod => (METHOD_TOKENS as string[]).includes(m));
    return { ...proxy, methods };
  }
  if (field.startsWith("method:")) {
    const [, methodToken, ...rest] = field.split(":");
    const method = methodToken as ProxyMethod;
    const tail = rest.join(":");
    const list = proxy.methodConfigs.map((entry) => ({ ...entry }));
    let entry = list.find((candidate) => candidate.method === method);
    if (!entry) {
      entry = { method, upstream: null, headers: null, query: null };
      list.push(entry);
    }
    if (tail === "upstream") {
      entry.upstream = value;
    } else if (tail.startsWith("header:") || tail.startsWith("query:")) {
      const isHeader = tail.startsWith("header:");
      const overrideKey = tail.slice(tail.indexOf(":") + 1);
      const next =
        (isHeader ? entry.headers : entry.query)?.filter((r) => r.key !== overrideKey) ?? [];
      if (value !== null) next.push({ key: overrideKey, value });
      if (isHeader) entry.headers = next;
      else entry.query = next;
    }
    const pruned = list.filter(
      (candidate) =>
        candidate.upstream !== null ||
        (candidate.headers?.length ?? 0) > 0 ||
        (candidate.query?.length ?? 0) > 0,
    );
    return { ...proxy, methodConfigs: pruned };
  }

  const [prefix, key] = [field.slice(0, field.indexOf(":")), field.slice(field.indexOf(":") + 1)];
  const listKey =
    prefix === "header" ? "headers" : prefix === "body" ? "bodyMerge" : "query";
  const rows = proxy[listKey].filter((row) => row.key !== key);
  if (value !== null) rows.push({ key, value });
  return { ...proxy, [listKey]: rows };
};

const methodSetEquals = (a: string, b: string) => {
  const norm = (v: string) =>
    v
      .split(",")
      .map((x) => x.trim())
      .filter(Boolean)
      .sort()
      .join(",");
  return norm(a) === norm(b);
};

const fieldValueEquals = (field: string, a: string | null, b: string | null) => {
  if (a === null || b === null) return a === null && b === null;
  return field === "methods" ? methodSetEquals(a, b) : a === b;
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

export const mockProxyService = {
  resetMockStore() {
    proxyStore = PROXY_MOCK_DATA.map((proxy) => ({ ...proxy }));
    proxyLogs = PROXY_MOCK_EXECUTION_LOGS.map((log) => ({ ...log }));
    proxyVersions = PROXY_MOCK_VERSION_HISTORY.map((version) => ({ ...version }));
  },

  getAll: async (params: ProxyListParams = {}): Promise<Proxy[]> => {
    await waitForMock();
    const search = params.searchKey?.trim().toLowerCase();
    if (!search) return proxyStore;
    return proxyStore.filter((proxy) =>
      [proxy.name, proxy.slug, proxy.upstreamMasked].some((value) =>
        value.toLowerCase().includes(search),
      ),
    );
  },

  get: async (id: string): Promise<Proxy | null> => {
    await waitForMock();
    return proxyStore.find((proxy) => proxy.id === id) ?? null;
  },

  create: async (values: ProxyFormValues): Promise<ProxyMutationResponse> => {
    await waitForMock();
    const proxy = buildProxy(values);
    proxyStore = [proxy, ...proxyStore];
    return { isSuccess: true, itemId: proxy.id, data: proxy, errors: null };
  },

  update: async ({ id, values }: { id: string; values: ProxyFormValues }) => {
    await waitForMock();
    const existing = proxyStore.find((proxy) => proxy.id === id);
    if (!existing) return { isSuccess: false, errors: "Proxy not found" };
    const next = buildProxy(values, existing);
    proxyStore = proxyStore.map((proxy) => (proxy.id === id ? next : proxy));
    return { isSuccess: true, itemId: id, data: next, errors: null };
  },

  toggle: async ({ id, enabled }: { id: string; enabled: boolean }) => {
    await waitForMock();
    const existing = proxyStore.find((proxy) => proxy.id === id);
    if (!existing) return { isSuccess: false, errors: "Proxy not found" };
    const next = { ...existing, enabled, updatedAt: new Date().toISOString() };
    proxyStore = proxyStore.map((proxy) => (proxy.id === id ? next : proxy));
    return { isSuccess: true, itemId: id, data: next, errors: null };
  },

  delete: async (id: string) => {
    await waitForMock();
    const exists = proxyStore.some((proxy) => proxy.id === id);
    if (!exists) return { isSuccess: false, errors: "Proxy not found" };
    proxyStore = proxyStore.filter((proxy) => proxy.id !== id);
    return { isSuccess: true, itemId: id, errors: null };
  },

  getExecutions: async (
    proxyId: string,
    filter: ProxyLogFilter,
    options: { page?: number; pageSize?: number } = {},
  ): Promise<ProxyExecutionPage> => {
    await waitForMock();
    const matched = proxyLogs.filter(
      (log) => log.proxyId === proxyId && matchesLogFilter(log.status, filter),
    );
    const pageSize = options.pageSize ?? 10;
    const page = options.page ?? 0;
    return {
      rows: matched.slice(page * pageSize, page * pageSize + pageSize),
      totalCount: matched.length,
    };
  },

  getExecution: async (
    proxyId: string,
    executionId: string,
  ): Promise<ProxyExecutionLog | null> => {
    await waitForMock();
    return (
      proxyLogs.find((log) => log.proxyId === proxyId && log.id === executionId) ?? null
    );
  },

  getOverview: async (proxyId: string): Promise<ProxyOverview | null> => {
    await waitForMock();
    const proxy = proxyStore.find((item) => item.id === proxyId);
    if (!proxy) return null;
    const rows = proxyLogs.filter((log) => log.proxyId === proxyId);
    const errorRatePct = rows.length
      ? Math.round((rows.filter((log) => log.status >= 400).length / rows.length) * 1000) / 10
      : 0;
    return {
      calls24h: proxy.calls24h,
      avgLatencyMs: rows.length
        ? Math.round(rows.reduce((sum, log) => sum + log.latencyMs, 0) / rows.length)
        : 0,
      errorRatePct,
      errorRateIsHigh: errorRatePct > 5,
      credentialRefs: [...proxy.headers, ...proxy.query, ...proxy.bodyMerge]
        .filter((row) => containsVarRef(row.value))
        .map((row) => row.value),
      methods: proxy.methods,
      lastCallAtUtc: rows[0]?.timeUtc ?? null,
    };
  },

  getVersions: async (proxyId: string): Promise<ProxyVersionHistory[]> => {
    await waitForMock();
    return proxyVersions.filter((version) => version.proxyId === proxyId);
  },

  revert: async ({ proxyId, versionId }: { proxyId: string; versionId: string }) => {
    await waitForMock();
    const version = proxyVersions.find((item) => item.proxyId === proxyId && item.id === versionId);
    const proxy = proxyStore.find((item) => item.id === proxyId);
    if (!proxy) {
      return {
        isSuccess: false,
        code: "PROXY_DELETED",
        message: "Proxy has been deleted.",
        errors: "Proxy has been deleted.",
      };
    }
    const changes: ProxyFieldChange[] = version?.changes ?? [];
    if (!version || version.kind === "delete" || version.kind === "create" || !changes.length) {
      return {
        isSuccess: false,
        code: "PROXY_VERSION_NOT_REVERTABLE",
        message: "This version has no change to revert.",
        errors: "This version has no change to revert.",
      };
    }

    const conflicts = changes.filter(
      (change) =>
        !fieldValueEquals(change.field, readProxyField(proxy, change.field), change.after ?? null),
    );
    if (conflicts.length) {
      return {
        isSuccess: false,
        code: "PROXY_REVERT_CONFLICT",
        message: `Cannot revert: ${conflicts
          .map((c) => c.label)
          .join(", ")} changed again in a later version.`,
        errors: Object.fromEntries(
          conflicts.map((c) => [c.field, "This field was changed again by a later version."]),
        ),
      };
    }

    let next = proxy;
    for (const change of changes) {
      next = applyProxyField(next, change.field, change.before ?? null);
    }
    next = { ...next, updatedAt: new Date().toISOString() };
    proxyStore = proxyStore.map((item) => (item.id === proxyId ? next : item));
    return { isSuccess: true, itemId: proxyId, data: next, errors: null };
  },

  test: async (request: ProxyTestRequest): Promise<ProxyTestResponse> => {
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
        responseBody: '{\n  "error": "Invalid upstream. Use https:// endpoints only."\n}',
      };
    }
    return {
      ok: true,
      status: 200,
      statusText: "OK",
      latencyMs: 142,
      meta: `${request.method} ${request.pathSuffix || "/"} via mock proxy`,
      responseBody: '{\n  "ok": true,\n  "source": "mock-proxy-test"\n}',
    };
  },

  exportExecutionsCsv: async ({
    proxyId,
    filter,
  }: {
    proxyId: string;
    filter: ProxyLogFilter;
  }): Promise<ProxyCsvExport> => {
    await waitForMock();
    const proxy = proxyStore.find((item) => item.id === proxyId);
    if (!proxy) throw new Error("Proxy not found");
    const rows = proxyLogs.filter(
      (log) => log.proxyId === proxyId && matchesLogFilter(log.status, filter),
    );
    return {
      fileName: `proxy-${proxy.slug}-executions.csv`,
      csv: toCsv(rows),
      rowCount: rows.length,
    };
  },
};
