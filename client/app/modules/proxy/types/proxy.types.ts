export type ProxyMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

export type ProxyKeyValue = {
  key: string;
  /**
   * Stored verbatim, including any `{{$VAR.name}}` configuration-variable token. The token is
   * resolved server-side on the forward / Test path only; nothing about a variable's value ever
   * reaches the console.
   */
  value: string;
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

/**
 * How the gateway treats the upstream JSON response body before relaying it. `"all"` — relay it
 * byte-for-byte (today's behaviour). `"select"` — project it to {@link Proxy.responseInclude}
 * (structural subset), fail-closed on a non-2xx / non-JSON / oversized upstream. Unlike
 * {@link ProxyBodyMode} this is a *persisted* field, not view-state.
 */
export type ProxyResponseMode = "all" | "select";

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
  /** Persisted. `"all"` ⇒ relay the upstream response unchanged. */
  responseMode: ProxyResponseMode;
  /** Field-path expressions kept when {@link responseMode} is `"select"` (`data.user.email`, `items[].id`). */
  responseInclude: string[];
  calls24h: number;
  createdAt?: string;
  updatedAt?: string;
};

/**
 * A node in the "Choose fields" tree builder. Component state only — never persisted (the form
 * stores the flattened {@link Proxy.responseInclude} path list) and never read off a response DTO.
 */
export type ResponseFieldNode = {
  /** Stable local uid. */
  id: string;
  /** Editable segment key (`""` while a freshly added row is unnamed). */
  key: string;
  /** Renders / stores as `key[]`. */
  isList: boolean;
  children: ResponseFieldNode[];
};

/**
 * Frontend-only view state for the "Request body" card. Never sent on a request DTO (the
 * mapper reads it to decide whether to emit `bodyMerge`, then drops it) and never read off a
 * response DTO (it is derived from `bodyMerge.length` on load).
 */
export type ProxyBodyMode = "passthrough" | "merge";

export type ProxyFormValues = Pick<
  Proxy,
  | "name"
  | "upstreamUrl"
  | "methods"
  | "headers"
  | "query"
  | "bodyMerge"
  | "methodConfigs"
  | "responseMode"
  | "responseInclude"
> & { bodyMode: ProxyBodyMode };

/**
 * The result of a "Fill from test connection" run — a Test executed with filtering forced off
 * (`responseMode: "all"`, `responseInclude: []`) so the sample is always the full response shape.
 */
export type SampleResult = {
  ok: boolean;
  status: number;
  contentType?: string;
  body: string;
  bytes: number;
  error?: string;
};

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

/** One page of the proxy list plus the server-side total, for the list screen's pagination. */
export type ProxyListPage = {
  items: Proxy[];
  totalCount: number;
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
};

export type ProxyKeyValueInputDto = {
  key: string;
  value: string;
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
  responseMode?: string | null;
  responseInclude?: string[] | null;
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
  responseFilterApplied?: boolean;
  responseFilterNote?: string | null;
  responseBodyBytes?: number;
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
  /** Server outcome enum, e.g. "Success" | "UpstreamUnreachable" | "VariableResolutionFailed". */
  outcome?: string;
  /** Short, safe diagnostic for a failed attempt; only populated on the on-demand detail row. */
  errorMessage?: string;
};

/** One page of the Request logs list: the mapped rows plus the unpaged 24h match count. */
export type ProxyExecutionPage = {
  rows: ProxyExecutionLog[];
  totalCount: number;
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
  /** Content-Type relayed to the client (`application/json; charset=utf-8` after a Select projection). */
  contentType?: string;
  /** `null` | `"Applied"` | `"EmptyResult"` | `"WholePrimitive"` | `"Failed"`. */
  responseFilterNote?: string | null;
  /** `true` iff a response filter ran and produced output for this Test. */
  responseFilterApplied?: boolean;
  /** Size of the body relayed to the client, in bytes (post-projection under Select). */
  responseBodyBytes: number;
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
