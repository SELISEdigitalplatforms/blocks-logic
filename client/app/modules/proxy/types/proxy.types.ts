export type ProxyMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

export type ProxyKeyValue = {
  key: string;
  value: string;
  isSecretRef?: boolean;
};

/**
 * A per-method override of the shared config. A `null` member inherits the shared
 * value; a non-null member (including an empty list) replaces it for that method.
 */
export type ProxyMethodOverride = {
  method: ProxyMethod;
  upstream: string | null;
  headers: ProxyKeyValue[] | null;
  query: ProxyKeyValue[] | null;
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
  /**
   * Fields merged into the top level of the client's JSON body on POST/PUT/PATCH forwards.
   * Empty ⇒ the body is forwarded byte-for-byte. There is no mode flag — pass-through is
   * `bodyMerge.length === 0` everywhere.
   */
  bodyMerge: ProxyKeyValue[];
  methodConfigs: ProxyMethodOverride[];
  calls24h: number;
  createdAt?: string;
  updatedAt?: string;
};

/**
 * Frontend-only view state for the "Request body" card. Never sent on a request DTO (the
 * mapper reads it to decide whether to emit `bodyMerge`, then drops it) and never read off a
 * response DTO (it is derived from `bodyMerge.length` on load).
 */
export type ProxyBodyMode = "passthrough" | "merge";

export type ProxyFormValues = Pick<
  Proxy,
  "name" | "upstreamUrl" | "methods" | "headers" | "query" | "bodyMerge" | "methodConfigs"
> & { bodyMode: ProxyBodyMode };

export type ProxyBackendDto = Record<string, unknown>;

export type ProxyError =
  | { code: "PROXY_VALIDATION"; message: string; errors?: Record<string, string> }
  | { code: "PROXY_NOT_FOUND"; message: string }
  | { code: "PROXY_REVERT_CONFLICT"; message: string; errors?: Record<string, string> };

/**
 * One field's `- before` / `+ after` pair in a change-history row. Values arrive
 * already masked from the server; `before == null` means the field/row did not
 * exist before, `after == null` means it was removed.
 */
export type ProxyFieldChange = {
  field: string;
  label: string;
  before?: string | null;
  after?: string | null;
};

export type ProxyListParams = {
  searchKey?: string;
  enabled?: boolean;
  pageNumber?: number;
  pageSize?: number;
};

export type ProxyLogFilter = "all" | "ok" | "client" | "server";

/** The `statusClass` values the server accepts on the execution endpoints. */
export type ProxyStatusClass = "all" | "2xx" | "4xx" | "5xx";

// ---------------------------------------------------------------------------
// Backend DTOs — mirror server/Proxy.DomainService/Dtos/*. The mapper is the
// only place these are turned into the frontend models above.
// ---------------------------------------------------------------------------

/** Blocks.Genesis BaseQueryListResponse<T>. */
export type BaseQueryListResponse<T> = {
  data: T | null;
  totalCount: number;
  errors?: Record<string, string> | null;
};

/** Blocks.Genesis BaseQueryResponse<T>. */
export type BaseQueryResponse<T> = {
  data: T | null;
  errors?: Record<string, string> | null;
};

/** Blocks.Genesis BaseMutationResponse (+ the SPEC §3.4 code/message). */
export type BaseMutationResponseDto = {
  isSuccess: boolean;
  itemId?: string | null;
  errors?: Record<string, string> | null;
  code?: string | null;
  message?: string | null;
};

export type ProxyKeyValueDto = {
  key: string;
  value: string;
  isSecretRef: boolean;
};

export type ProxyKeyValueInputDto = {
  key: string;
  value: string;
  isSecretRef: boolean;
};

/** Mirrors server `ProxyMethodConfigDto` / `ProxyMethodConfigInputDto`. */
export type ProxyMethodConfigDto = {
  method: string;
  upstream?: string | null;
  headers?: ProxyKeyValueDto[] | null;
  query?: ProxyKeyValueDto[] | null;
};

export type ProxyListItemDto = {
  itemId: string;
  name: string;
  slug: string;
  upstreamMasked: string;
  methods: string[];
  enabled: boolean;
  injectedCredential: boolean;
  headerCount: number;
  queryCount: number;
  calls24h: number;
  createdDate: string;
  lastUpdatedDate: string;
};

export type ProxyDetailDto = {
  itemId: string;
  name: string;
  slug: string;
  path: string;
  upstream: string;
  upstreamMasked: string;
  methods: string[];
  enabled: boolean;
  headers: ProxyKeyValueDto[];
  query: ProxyKeyValueDto[];
  bodyMerge?: ProxyKeyValueDto[] | null;
  methodConfigs: ProxyMethodConfigDto[];
  currentVersion: number;
  createdDate: string;
  createdBy?: string | null;
  lastUpdatedDate: string;
  lastUpdatedBy?: string | null;
};

export type ProxyVersionDto = {
  itemId: string;
  versionNumber: number;
  /** "Create" | "ConfigUpdate" | "Toggle" | "Revert" | "Delete". */
  kind: string;
  changeSummary: string;
  changes: ProxyFieldChange[];
  who?: string | null;
  /** Display name captured at write time; null for older rows / system changes. */
  whoName?: string | null;
  whenUtc: string;
  versionLabel: string;
};

export type ProxyExecutionListItemDto = {
  itemId: string;
  startedAtUtc: string;
  requestMethod: string;
  requestPath: string;
  statusCode: number;
  latencyMs: number;
  outcome: string;
  upstreamHost: string;
};

export type ProxyExecutionDetailDto = {
  itemId: string;
  proxyId: string;
  proxySlug: string;
  startedAtUtc: string;
  finishedAtUtc: string;
  latencyMs: number;
  requestMethod: string;
  requestPath: string;
  requestQuery: string;
  upstreamUrl: string;
  upstreamHost: string;
  injectedHeaderKeys: string[];
  injectedQueryKeys: string[];
  statusCode: number;
  upstreamStatusCode?: number | null;
  outcome: string;
  errorMessage?: string | null;
  responseContentType?: string | null;
  responseBodyBytes: number;
  responseBody?: string | null;
  responseBodyTruncatedForDisplay: boolean;
};

export type ProxyOverviewDto = {
  calls24h: number;
  avgLatencyMs: number;
  errorRatePct: number;
  errorRateIsHigh: boolean;
  credentialRefs: string[];
  methods: string[];
  lastCallAtUtc?: string | null;
};

export type ProxyTestResponseDto = {
  ok: boolean;
  status: number;
  outcome: string;
  latencyMs: number;
  upstreamUrl: string;
  upstreamHost: string;
  injectedHeaderKeys: string[];
  injectedQueryKeys: string[];
  responseContentType?: string | null;
  responseBody?: string | null;
  errorMessage?: string | null;
};

export type ProxyOverview = {
  calls24h: number;
  avgLatencyMs: number;
  errorRatePct: number;
  errorRateIsHigh: boolean;
  credentialRefs: string[];
  methods: ProxyMethod[];
  lastCallAtUtc: string | null;
};

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
  /** User id of who made the change; falls back to "Unknown". */
  actor: string;
  /** Display name captured at write time, if the server recorded one. */
  actorName?: string;
  whenUtc: string;
  changes: ProxyFieldChange[];
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
  /** Stable failure code from SPEC §3.4 (e.g. PROXY_SLUG_CONFLICT); null on success. */
  code?: string | null;
  /** Human-readable failure message when the server provides one. */
  message?: string | null;
};
