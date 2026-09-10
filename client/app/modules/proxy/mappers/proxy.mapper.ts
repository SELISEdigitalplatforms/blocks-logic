import { isResponsePath, maskUpstreamUrl } from "../utils";
import {
  BaseMutationResponseDto,
  Proxy,
  ProxyDetailDto,
  ProxyExecutionDetailDto,
  ProxyExecutionListItemDto,
  ProxyExecutionLog,
  ProxyFormValues,
  ProxyKeyValue,
  ProxyKeyValueDto,
  ProxyKeyValueInputDto,
  ProxyLogFilter,
  ProxyListItemDto,
  ProxyMethod,
  ProxyMethodConfigDto,
  ProxyMethodOverride,
  ProxyMutationResponse,
  ProxyFieldChange,
  ProxyOverview,
  ProxyOverviewDto,
  ProxyStatusClass,
  ProxyTestRequest,
  ProxyTestResponse,
  ProxyTestResponseDto,
  ProxyVersionDto,
  ProxyVersionHistory,
} from "../types";

const VALID_METHODS: ProxyMethod[] = ["GET", "POST", "PUT", "PATCH", "DELETE"];

const isMethod = (value: unknown): value is ProxyMethod =>
  typeof value === "string" && VALID_METHODS.includes(value as ProxyMethod);

const toMethods = (value: string[] | null | undefined): ProxyMethod[] => {
  const methods = (value ?? []).filter(isMethod);
  return methods.length ? methods : ["GET"];
};

const HTTP_STATUS_TEXT: Record<number, string> = {
  200: "OK",
  201: "Created",
  202: "Accepted",
  204: "No Content",
  301: "Moved Permanently",
  302: "Found",
  304: "Not Modified",
  400: "Bad Request",
  401: "Unauthorized",
  403: "Forbidden",
  404: "Not Found",
  405: "Method Not Allowed",
  409: "Conflict",
  413: "Payload Too Large",
  429: "Too Many Requests",
  500: "Internal Server Error",
  502: "Bad Gateway",
  503: "Service Unavailable",
  504: "Gateway Timeout",
};

const statusText = (status: number) =>
  HTTP_STATUS_TEXT[status] ??
  (status >= 500 ? "Server Error" : status >= 400 ? "Request Error" : "OK");

// ---------------------------------------------------------------------------
// Filters
// ---------------------------------------------------------------------------

/** Frontend log filter -> the `statusClass` the server's execution endpoints expect. */
export const mapLogFilterToStatusClass = (filter: ProxyLogFilter): ProxyStatusClass => {
  switch (filter) {
    case "ok":
      return "2xx";
    case "client":
      return "4xx";
    case "server":
      return "5xx";
    default:
      return "all";
  }
};

// ---------------------------------------------------------------------------
// Request payloads (ProxyController Create / Update / Test)
// ---------------------------------------------------------------------------

const toKeyValueInputs = (rows: ProxyKeyValue[]): ProxyKeyValueInputDto[] =>
  rows
    .map((row) => ({
      key: row.key.trim(),
      value: row.value.trim(),
    }))
    .filter((row) => row.key || row.value);

/**
 * Per-method overrides -> the request array. Keeps only overrides for a still-selected
 * method, trims each member, and drops an entry that ends up inheriting everything
 * (blank upstream + empty/absent lists) so it matches what the server would persist.
 */
const toMethodConfigInputs = (
  overrides: ProxyMethodOverride[] | null | undefined,
  selectedMethods: ProxyMethod[],
) =>
  (overrides ?? [])
    .filter((entry) => selectedMethods.includes(entry.method))
    .map((entry) => {
      const upstream = entry.upstream?.trim() || null;
      const headers = entry.headers ? toKeyValueInputs(entry.headers) : null;
      const query = entry.query ? toKeyValueInputs(entry.query) : null;
      return {
        method: entry.method,
        upstream,
        headers: headers && headers.length ? headers : null,
        query: query && query.length ? query : null,
      };
    })
    .filter((entry) => entry.upstream || entry.headers || entry.query);

/**
 * Body-merge rows only reach the wire when the "Merge fields" tab is the selected one. On the
 * "Pass through" tab the rows the user may have typed are excluded — the request carries
 * `bodyMerge: []`, identical to never having entered anything (locked decision §2.2). `bodyMode`
 * itself is never added to the payload.
 */
const toBodyMergeInputs = (values: ProxyFormValues): ProxyKeyValueInputDto[] =>
  values.bodyMode === "merge" ? toKeyValueInputs(values.bodyMerge) : [];

/**
 * Response-field-filter payload. A "select" save emits `Select` + the trimmed / deduped / valid
 * path list (an empty list is legal — the server relays `{}` then). "all" always emits `All` + `[]`.
 */
const toResponseFilter = (values: ProxyFormValues) =>
  values.responseMode === "select"
    ? {
        responseMode: "Select",
        responseInclude: [
          ...new Set(values.responseInclude.map((path) => path.trim()).filter(isResponsePath)),
        ],
      }
    : { responseMode: "All", responseInclude: [] };

export const mapProxyToCreatePayload = (values: ProxyFormValues) => ({
  name: values.name.trim(),
  upstream: values.upstreamUrl.trim(),
  methods: values.methods,
  headers: toKeyValueInputs(values.headers),
  query: toKeyValueInputs(values.query),
  bodyMerge: toBodyMergeInputs(values),
  methodConfigs: toMethodConfigInputs(values.methodConfigs, values.methods),
  ...toResponseFilter(values),
  enabled: true,
});

export const mapProxyToUpdatePayload = (id: string, values: ProxyFormValues) => ({
  itemId: id,
  name: values.name.trim(),
  upstream: values.upstreamUrl.trim(),
  methods: values.methods,
  headers: toKeyValueInputs(values.headers),
  query: toKeyValueInputs(values.query),
  bodyMerge: toBodyMergeInputs(values),
  methodConfigs: toMethodConfigInputs(values.methodConfigs, values.methods),
  ...toResponseFilter(values),
});

export const mapProxyTestRequestToPayload = (request: ProxyTestRequest) => ({
  proxyId: request.proxyId,
  draft: request.draft
    ? {
        upstream: request.draft.upstreamUrl.trim(),
        methods: request.draft.methods,
        headers: toKeyValueInputs(request.draft.headers),
        query: toKeyValueInputs(request.draft.query),
        bodyMerge: toBodyMergeInputs(request.draft),
        methodConfigs: toMethodConfigInputs(request.draft.methodConfigs, request.draft.methods),
        ...toResponseFilter(request.draft),
      }
    : undefined,
  method: request.method,
  pathSuffix: request.pathSuffix ?? "",
  query: request.query ?? "",
  body: request.body || undefined,
  contentType: request.contentType,
});

// ---------------------------------------------------------------------------
// Response DTOs -> frontend models
// ---------------------------------------------------------------------------

const toKeyValues = (dtos: ProxyKeyValueDto[] | null | undefined): ProxyKeyValue[] =>
  (dtos ?? []).map((dto) => ({
    key: dto.key,
    value: dto.value,
  }));

const toMethodOverrides = (
  dtos: ProxyMethodConfigDto[] | null | undefined,
): ProxyMethodOverride[] =>
  (dtos ?? [])
    .filter((dto): dto is ProxyMethodConfigDto => isMethod(dto?.method))
    .map((dto) => ({
      method: dto.method as ProxyMethod,
      upstream: dto.upstream ?? null,
      headers: dto.headers ? toKeyValues(dto.headers) : null,
      query: dto.query ? toKeyValues(dto.query) : null,
    }));

/** One row of `POST /api/Proxy/GetAll` (no full upstream / header rows in the list projection). */
export const mapProxyListItemDtoToProxy = (dto: ProxyListItemDto): Proxy => ({
  id: dto.itemId,
  name: dto.name,
  slug: dto.slug,
  upstreamUrl: "",
  upstreamMasked: dto.upstreamMasked,
  methods: toMethods(dto.methods),
  enabled: dto.enabled,
  headers: [],
  query: [],
  bodyMerge: [],
  methodConfigs: [],
  responseMode: "all",
  responseInclude: [],
  calls24h: Number(dto.calls24h ?? 0),
  createdAt: dto.createdDate,
  updatedAt: dto.lastUpdatedDate,
});

/** `GET /api/Proxy/Get` — the full configuration for the form / detail view. */
export const mapProxyDetailDtoToProxy = (dto: ProxyDetailDto): Proxy => ({
  id: dto.itemId,
  name: dto.name,
  slug: dto.slug,
  upstreamUrl: dto.upstream,
  upstreamMasked: dto.upstreamMasked || maskUpstreamUrl(dto.upstream),
  methods: toMethods(dto.methods),
  enabled: dto.enabled,
  headers: toKeyValues(dto.headers),
  query: toKeyValues(dto.query),
  bodyMerge: toKeyValues(dto.bodyMerge),
  methodConfigs: toMethodOverrides(dto.methodConfigs),
  responseMode: dto.responseMode?.toLowerCase() === "select" ? "select" : "all",
  responseInclude: Array.isArray(dto.responseInclude) ? dto.responseInclude : [],
  calls24h: 0,
  createdAt: dto.createdDate,
  updatedAt: dto.lastUpdatedDate,
});

const toFieldChanges = (
  value: ProxyVersionDto["changes"] | null | undefined,
): ProxyFieldChange[] =>
  Array.isArray(value)
    ? value.map((c) => ({
        field: c.field,
        label: c.label,
        before: c.before ?? null,
        after: c.after ?? null,
      }))
    : [];

const VERSION_KIND: Record<string, ProxyVersionHistory["kind"]> = {
  Create: "create",
  ConfigUpdate: "edit",
  Toggle: "toggle",
  Revert: "revert",
  Delete: "delete",
};

/** One row of `POST /api/Proxy/GetVersions`. `proxyId` comes from the request, not the DTO. */
export const mapProxyVersionDtoToHistory = (
  dto: ProxyVersionDto,
  proxyId: string,
): ProxyVersionHistory => ({
  id: dto.itemId,
  proxyId,
  versionLabel: dto.versionLabel || `v${dto.versionNumber}`,
  versionNumber: Number(dto.versionNumber ?? 0),
  kind: VERSION_KIND[dto.kind] ?? "edit",
  summary: dto.changeSummary ?? "",
  actor: dto.who ?? "Unknown",
  actorName: dto.whoName ?? undefined,
  whenUtc: dto.whenUtc,
  changes: toFieldChanges(dto.changes),
});

/**
 * One row of `POST /api/Proxy/GetExecutions`. The list projection carries no upstream URL,
 * injected keys or body — those arrive via {@link mapProxyExecutionDetailDtoToLog}.
 */
export const mapProxyExecutionListItemDtoToLog = (
  dto: ProxyExecutionListItemDto,
  proxyId: string,
): ProxyExecutionLog => ({
  id: dto.itemId,
  proxyId,
  timeUtc: dto.startedAtUtc,
  method: isMethod(dto.requestMethod) ? dto.requestMethod : "GET",
  path: dto.requestPath,
  status: Number(dto.statusCode ?? 0),
  statusText: statusText(Number(dto.statusCode ?? 0)),
  latencyMs: Number(dto.latencyMs ?? 0),
  upstreamHost: dto.upstreamHost ?? "",
  upstreamUrl: "",
  injectedHeaderKeys: [],
  injectedQueryKeys: [],
  responseBody: "",
  responseContentType: undefined,
  outcome: dto.outcome ?? undefined,
});

/** `GET /api/Proxy/GetExecution` — the expanded row with the display-clipped response body. */
export const mapProxyExecutionDetailDtoToLog = (
  dto: ProxyExecutionDetailDto,
): ProxyExecutionLog => ({
  id: dto.itemId,
  proxyId: dto.proxyId,
  timeUtc: dto.startedAtUtc,
  method: isMethod(dto.requestMethod) ? dto.requestMethod : "GET",
  path: dto.requestPath,
  status: Number(dto.statusCode ?? 0),
  statusText: statusText(Number(dto.statusCode ?? 0)),
  latencyMs: Number(dto.latencyMs ?? 0),
  upstreamHost: dto.upstreamHost ?? "",
  upstreamUrl: dto.upstreamUrl ?? "",
  injectedHeaderKeys: Array.isArray(dto.injectedHeaderKeys) ? dto.injectedHeaderKeys : [],
  injectedQueryKeys: Array.isArray(dto.injectedQueryKeys) ? dto.injectedQueryKeys : [],
  responseBody: dto.responseBody ?? "",
  responseContentType: dto.responseContentType ?? undefined,
  outcome: dto.outcome ?? undefined,
  errorMessage: dto.errorMessage ?? undefined,
});

/** `POST /api/Proxy/GetOverview` — the rolling 24h tiles for the detail view. */
export const mapProxyOverviewDtoToOverview = (dto: ProxyOverviewDto): ProxyOverview => ({
  calls24h: Number(dto.calls24h ?? 0),
  avgLatencyMs: Number(dto.avgLatencyMs ?? 0),
  errorRatePct: Number(dto.errorRatePct ?? 0),
  errorRateIsHigh: Boolean(dto.errorRateIsHigh),
  credentialRefs: Array.isArray(dto.credentialRefs) ? dto.credentialRefs : [],
  methods: toMethods(dto.methods),
  lastCallAtUtc: dto.lastCallAtUtc ?? null,
});

/** 200 body of `POST /api/Proxy/Test` -> the shape the test panel renders. */
export const mapProxyTestResponseDtoToResponse = (
  dto: ProxyTestResponseDto,
  request: ProxyTestRequest,
): ProxyTestResponse => {
  const host = dto.upstreamHost || (dto.upstreamUrl ? safeHost(dto.upstreamUrl) : "");
  const filterMeta = responseFilterMeta(dto.responseFilterNote);
  const meta =
    dto.errorMessage ||
    `${request.method} ${request.pathSuffix || "/"}${host ? ` → ${host}` : ""}${
      filterMeta ? ` · ${filterMeta}` : ""
    }`;
  return {
    ok: Boolean(dto.ok),
    status: Number(dto.status ?? 0),
    statusText: statusText(Number(dto.status ?? 0)),
    latencyMs: Number(dto.latencyMs ?? 0),
    meta,
    responseBody: dto.responseBody ?? dto.errorMessage ?? "",
    contentType: dto.responseContentType ?? undefined,
    responseFilterNote: dto.responseFilterNote ?? null,
    responseFilterApplied: Boolean(dto.responseFilterApplied),
    responseBodyBytes: Number(dto.responseBodyBytes ?? 0),
  };
};

/** Short human tag for the Test-result meta line, from the server's `ResponseFilterNote`. */
const responseFilterMeta = (note: string | null | undefined): string | null => {
  switch (note) {
    case "Applied":
      return "filter: applied";
    case "EmptyResult":
      return "filter: empty-result";
    case "WholePrimitive":
      return "filter: whole-primitive";
    case "Failed":
      return "filter: FAILED";
    default:
      return null;
  }
};

const safeHost = (url: string) => {
  try {
    return new URL(url).host;
  } catch {
    return "";
  }
};

/** BaseMutationResponse (+ code/message) -> the frontend mutation result. */
export const mapMutationResponse = (dto: BaseMutationResponseDto): ProxyMutationResponse => ({
  isSuccess: Boolean(dto.isSuccess),
  itemId: dto.itemId ?? undefined,
  errors: dto.errors ?? dto.message ?? null,
  code: dto.code ?? null,
  message: dto.message ?? null,
});
