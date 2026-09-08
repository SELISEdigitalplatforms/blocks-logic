import { ProxyMethod } from "../types";

export const PROXY_ENDPOINTS = {
  GET_ALL: "/api/Proxy/GetAll",
  GET: "/api/Proxy/Get",
  CREATE: "/api/Proxy/Create",
  UPDATE: "/api/Proxy/Update",
  UPDATE_STATE: "/api/Proxy/UpdateState",
  DELETE: "/api/Proxy/Delete",
  GET_VERSIONS: "/api/Proxy/GetVersions",
  REVERT: "/api/Proxy/Revert",
  TEST: "/api/Proxy/Test",
  GET_EXECUTIONS: "/api/Proxy/GetExecutions",
  GET_EXECUTION: "/api/Proxy/GetExecution",
  GET_OVERVIEW: "/api/Proxy/GetOverview",
  EXPORT_EXECUTIONS_CSV: "/api/Proxy/ExportExecutionsCsv",
} as const;

export const PROXY_METHODS: ProxyMethod[] = ["GET", "POST", "PUT", "PATCH", "DELETE"];

export const PROXY_QUERY_KEY = ["proxies"] as const;

/** Page sizes offered on the Request logs table (mirrors the server's 1..200 cap). */
export const PROXY_LOG_PAGE_SIZE_OPTIONS = [10, 20, 50] as const;

/** Default `pageSize` for `GetExecutions` — the first option above. */
export const PROXY_LOG_PAGE_SIZE = PROXY_LOG_PAGE_SIZE_OPTIONS[0];

/** The Phase 2 data-plane route a tenant's client actually calls (see ProxyGatewayController). */
export const getProxyClientPath = (slug: string) => `/api/proxy/gateway/${slug || "proxy-name"}/*`;
