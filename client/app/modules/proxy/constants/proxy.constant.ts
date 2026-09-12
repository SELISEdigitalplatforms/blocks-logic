import { getProjectBlocksApiUrl, getRuntimeEnv, type IProject } from "@seliseblocks/genesis-os";

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
 * Service and version segment the public API gateway routes on. The service's own route is
 * `~/api/proxy/gateway/{slug}/{**path}`; the public gateway addresses it under this prefix instead,
 * so the `/api` segment never appears in a URL we hand to a tenant.
 */
const PUBLIC_GATEWAY_PREFIX = "/logic/v4";

/**
 * The data-plane path a tenant's client actually calls (see `ProxiesController.Gateway`). Pinned
 * server-side: it is a published contract, not a convention-derived URL.
 */
export const getProxyClientPath = (slug: string, routePath?: string) => {
  const root = `${PUBLIC_GATEWAY_PREFIX}/proxy/gateway/${slug || "proxy-name"}`;
  const suffix = (routePath ?? "").replace(/^\/+|\/+$/g, "");
  return suffix ? `${root}/${suffix}` : root;
};

/**
 * The host a tenant's own client calls, which is per app rather than per environment:
 * `blocksapi.<custom domain>` once the project has one, else the shared public API host. Never the
 * console's own `BLOCKS_LOGIC_BASE_URL`, which is identical for every tenant in an environment and so
 * is wrong to publish as a customer-facing address.
 *
 * `getProjectBlocksApiUrl` reads `window.process.env` directly rather than going through
 * `getRuntimeEnv`, so it returns `""` in any app that only populates `window.__BLOCKS_ENV__`. The
 * `getRuntimeEnv` fallback covers that; both read the same key.
 */
export const getProxyPublicHost = (project?: IProject | null): string => {
  // `getProjectBlocksApiUrl` builds `"blocksapi." + getDomain(customDomain)`, and `getDomain` only
  // accepts a URL carrying a scheme: a bare `acme.com` yields `""`, leaving the truthy but unusable
  // host `"blocksapi."`. Require a real dotted hostname before trusting it, so that case falls back
  // to the shared public host instead of publishing a broken address.
  const fromProject = getProjectBlocksApiUrl(project ?? undefined);
  const usable = /\.[a-z]{2,}$/i.test(fromProject.replace(/\/+$/, "")) ? fromProject : "";
  const host = usable || getRuntimeEnv("BLOCKS_PUBLIC_API_BASE_URL");
  if (!host) return "";
  return /^https?:\/\//i.test(host) ? host : `https://${host}`;
};

/** Full customer-facing URL for one proxy, or just the path when no public host is configured yet. */
export const getProxyClientUrl = (
  project: IProject | null | undefined,
  slug: string,
  routePath?: string,
) => `${getProxyPublicHost(project)}${getProxyClientPath(slug, routePath)}`;
