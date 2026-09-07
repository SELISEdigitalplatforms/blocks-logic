export type ProxyMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

export type ProxyKeyValue = {
  key: string;
  value: string;
  isSecretRef?: boolean;
};

export type ProxyStatus = "live" | "paused";

export type Proxy = {
  id: string;
  name: string;
  slug: string;
  upstreamUrl: string;
  upstreamMasked: string;
  methods: ProxyMethod[];
  enabled: boolean;
  headers: ProxyKeyValue[];
  query: ProxyKeyValue[];
  calls24h: number;
  createdAt?: string;
  updatedAt?: string;
};

export type ProxyFormValues = Pick<
  Proxy,
  "name" | "upstreamUrl" | "methods" | "headers" | "query"
>;

export type ProxyBackendDto = Record<string, unknown>;

export type ProxyError =
  | { code: "PROXY_VALIDATION"; message: string; errors?: Record<string, string> }
  | { code: "PROXY_NOT_FOUND"; message: string };

export type ProxyListParams = {
  searchKey?: string;
};

export type ProxyLogFilter = "all" | "ok" | "client" | "server";

export type ProxyExecutionLog = {
  id: string;
  proxyId: string;
  timeUtc: string;
  method: ProxyMethod;
  path: string;
  status: number;
  statusText: string;
  latencyMs: number;
  upstreamHost: string;
  upstreamUrl: string;
  injectedHeaderKeys: string[];
  injectedQueryKeys: string[];
  responseBody: string;
  responseContentType?: string;
};

export type ProxyVersionHistory = {
  id: string;
  proxyId: string;
  versionLabel: string;
  versionNumber: number;
  kind: "create" | "edit" | "toggle" | "revert" | "delete";
  summary: string;
  actor: string;
  whenUtc: string;
  before?: string | null;
  after?: string | null;
};

export type ProxyTestRequest = {
  proxyId?: string;
  draft?: ProxyFormValues;
  method: ProxyMethod;
  pathSuffix?: string;
  query?: string;
  body?: string;
  contentType?: string;
};

export type ProxyTestResponse = {
  ok: boolean;
  status: number;
  statusText: string;
  latencyMs: number;
  meta: string;
  responseBody: string;
};

export type ProxyCsvExport = {
  fileName: string;
  csv: string;
  rowCount: number;
};

export type ProxyMutationResponse = {
  isSuccess: boolean;
  itemId?: string;
  data?: Proxy;
  errors?: string | Record<string, string> | null;
};
