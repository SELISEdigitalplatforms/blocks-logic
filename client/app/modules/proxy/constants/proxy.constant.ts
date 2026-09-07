import { ProxyMethod } from "../types";

export const PROXY_ENDPOINTS = {
  GET_ALL: "/api/Proxy/GetAll",
  GET: "/api/Proxy/Get",
  CREATE: "/api/Proxy/Create",
  UPDATE: "/api/Proxy/Update",
  TOGGLE: "/api/Proxy/Toggle",
  DELETE: "/api/Proxy/Delete",
  GET_EXECUTIONS: "/api/Proxy/GetExecutions",
  GET_VERSIONS: "/api/Proxy/GetVersions",
  REVERT: "/api/Proxy/Revert",
  TEST: "/api/Proxy/Test",
  EXPORT_EXECUTIONS_CSV: "/api/Proxy/ExportExecutionsCsv",
} as const;

export const PROXY_METHODS: ProxyMethod[] = ["GET", "POST", "PUT", "PATCH", "DELETE"];

export const PROXY_QUERY_KEY = ["proxies"] as const;

export const getProxyClientPath = (slug: string) => `/api/proxy/gateway/${slug || "proxy-name"}/*`;
