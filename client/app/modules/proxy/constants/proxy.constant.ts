import { ProxyMethod } from "../types";

/** Control-plane resource root. One proxy is `${PROXY_BASE}/{proxyId}`; everything else hangs off that. */
const PROXY_BASE = "/api/Proxies";

const id = (value: string) => encodeURIComponent(value);

/**
 * The control-plane URLs (see `ProxiesController`). The collection and the draft-test endpoint are fixed
 * strings; everything scoped to one proxy is a builder, because the id belongs in the path rather than in
 * the payload.
 */
export const PROXY_ENDPOINTS = {
  /** `GET` lists the tenant's proxies (filters as query params); `POST` creates one. */
  COLLECTION: PROXY_BASE,
  /**
   * `POST` — test a saved proxy or an unsaved draft. It sits on the collection rather than under
   * `{proxyId}` precisely because the draft being tested need not exist yet.
   */
  TEST: `${PROXY_BASE}/test`,
  /** One proxy: `GET`, `PUT` (full update), `PATCH` (enabled only), `DELETE`. */
  byId: (proxyId: string) => `${PROXY_BASE}/${id(proxyId)}`,
  /** `GET` — the Change history tab. */
  versions: (proxyId: string) => `${PROXY_BASE}/${id(proxyId)}/versions`,
  /** `POST` — restore that version's config as a new version row. */
  revert: (proxyId: string, versionId: string) =>
    `${PROXY_BASE}/${id(proxyId)}/versions/${id(versionId)}/revert`,
  /** `GET` — the Request logs tab (filters as query params). */
  executions: (proxyId: string) => `${PROXY_BASE}/${id(proxyId)}/executions`,
  /** `GET` — one expanded request-log row. */
  execution: (proxyId: string, executionId: string) =>
    `${PROXY_BASE}/${id(proxyId)}/executions/${id(executionId)}`,
  /** `GET` — the filtered log as a CSV attachment. */
  /** `GET` — the Overview tiles. */
  overview: (proxyId: string) => `${PROXY_BASE}/${id(proxyId)}/overview`,
} as const;

export const PROXY_METHODS: ProxyMethod[] = ["GET", "POST", "PUT", "PATCH", "DELETE"];

export const PROXY_QUERY_KEY = ["proxies"] as const;

/** Page sizes offered on the Request logs table (mirrors the server's 1..200 cap). */
export const PROXY_LOG_PAGE_SIZE_OPTIONS = [10, 20, 50] as const;

/** Default `pageSize` for the executions list — the first option above. */
export const PROXY_LOG_PAGE_SIZE = PROXY_LOG_PAGE_SIZE_OPTIONS[0];

/**
 * The Phase 2 data-plane route a tenant's client actually calls (see `ProxiesController.Gateway`). This one
 * is deliberately lowercase and pinned server-side: it is a published contract, not a convention-derived URL.
 */
export const getProxyClientPath = (slug: string) => `/api/proxy/gateway/${slug || "proxy-name"}/*`;
