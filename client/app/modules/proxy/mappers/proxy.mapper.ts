import { compactKeyValues, defaultProxyAccess, isResponsePath, maskUpstreamUrl } from "../utils";
import { PROXY_METHODS } from "../constants";
import {
  BaseMutationResponseDto,
  Proxy,
  ProxyAccess,
  ProxyAccessDto,
  ProxyAccessRule,
  ProxyAccessRuleDto,
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
  ProxyOpenApiPreview,
  ProxyOpenApiPreviewDto,
  ProxyOverviewDto,
  ProxyRoute,
  ProxyRouteDto,
  ProxyStatusClass,
  ProxyTestRequest,
  ProxyTestResponse,
  ProxyTestResponseDto,
  ProxyVersionDto,
  ProxyVersionHistory,
  ProxyResilience,
  ProxyResilienceDto,
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
// Request payloads (ProxiesController Create / Update / Test)
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

// ---------------------------------------------------------------------------
// "Who can call it" — the server's wire vocabulary lives here and nowhere else.
// ---------------------------------------------------------------------------

const toAccessRulePayload = (rule: ProxyAccessRule): ProxyAccessRuleDto => ({
  mode: rule.mode === "all" ? "all" : "any",
  values: [...new Set(rule.values.map((value) => value.trim()).filter(Boolean))],
});

/**
 * A public save never carries role / permission values (the server rejects that combination), so
 * chips a user picked before switching to Public are dropped here rather than failing the save.
 */
export const toAccessPayload = (access: ProxyAccess | undefined): ProxyAccessDto => {
  const value = access ?? defaultProxyAccess();
  const isPublic = value.kind === "public";
  return {
    kind: isPublic ? "Public" : "BlocksToken",
    combine: value.combine === "and" ? "And" : "Or",
    roles: isPublic ? { mode: "any", values: [] } : toAccessRulePayload(value.roles),
    permissions: isPublic ? { mode: "any", values: [] } : toAccessRulePayload(value.permissions),
    organizationId: value.organizationId?.trim() ?? "",
  };
};

const toAccessRule = (dto: ProxyAccessRuleDto | null | undefined): ProxyAccessRule => ({
  mode: dto?.mode?.toLowerCase() === "all" ? "all" : "any",
  values: Array.isArray(dto?.values)
    ? dto.values.filter((value): value is string => typeof value === "string")
    : [],
});

/** A missing block (older proxy) is the token-only default — exactly what the server enforces for it. */
export const toAccess = (dto: ProxyAccessDto | null | undefined): ProxyAccess =>
  dto
    ? {
        kind: dto.kind?.toLowerCase() === "public" ? "public" : "blocksToken",
        combine: dto.combine?.toLowerCase() === "and" ? "and" : "or",
        roles: toAccessRule(dto.roles),
        permissions: toAccessRule(dto.permissions),
        organizationId: dto.organizationId ?? "",
      }
    : defaultProxyAccess();

export const mapProxyToCreatePayload = (values: ProxyFormValues) => ({
  name: values.name.trim(),
  upstream: values.upstreamUrl.trim(),
  methods: values.methods,
  headers: toKeyValueInputs(values.headers),
  query: toKeyValueInputs(values.query),
  bodyMerge: toBodyMergeInputs(values),
  methodConfigs: toMethodConfigInputs(values.methodConfigs, values.methods),
  routes: toRouteInputs(values.routes),
  ...toResponseFilter(values),
  access: toAccessPayload(values.access),
  resilience: toResilienceInput(values.resilience ?? null),
  requestsPerMinute: values.requestsPerMinute ?? null,
  enabled: true,
});

export const mapProxyToUpdatePayload = (
  id: string,
  values: ProxyFormValues,
  expectedVersion: number | null = null,
) => ({
  itemId: id,
  name: values.name.trim(),
  upstream: values.upstreamUrl.trim(),
  methods: values.methods,
  headers: toKeyValueInputs(values.headers),
  query: toKeyValueInputs(values.query),
  bodyMerge: toBodyMergeInputs(values),
  methodConfigs: toMethodConfigInputs(values.methodConfigs, values.methods),
  routes: toRouteInputs(values.routes),
  ...toResponseFilter(values),
  access: toAccessPayload(values.access),
  resilience: toResilienceInput(values.resilience ?? null),
  // Always sent: the server reads an omitted value as "back to the default".
  requestsPerMinute: values.requestsPerMinute ?? null,
  // The version the form was loaded from; the server answers 409 if the proxy changed since (PX-16).
  expectedVersion,
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
        // Without the routes the server tests against the base path only, and a Test of any other
        // endpoint is refused as unlisted before it ever reaches the vendor.
        routes: toRouteInputs(request.draft.routes),
        ...toResponseFilter(request.draft),
        access: toAccessPayload(request.draft.access),
        // A Test has to run under the policy the form is showing, or it answers a different question
        // than the one being asked. The server validates it exactly as it does on Save.
        resilience: toResilienceInput(request.draft.resilience ?? null),
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

/** One row of `GET /api/Proxies` (no full upstream / header rows in the list projection). */
/** Strips leading and trailing slashes; the server stores and matches templates without them. */
const trimSlashes = (value: string | null | undefined) => (value ?? "").replace(/^\/+|\/+$/g, "");

/**
 * `null` and `[]` are different on a route override: `null` inherits the proxy-wide value, `[]` is an
 * explicit "none". Collapsing them would turn a route that opted out of the body merge back into one
 * that inherits it, so the distinction is preserved in both directions.
 */
const toRouteOverride = (value: ProxyKeyValueDto[] | null | undefined): ProxyKeyValue[] | null =>
  Array.isArray(value)
    ? value.map((row) => ({ key: row.key ?? "", value: row.value ?? "" }))
    : null;

const toRoutes = (value: ProxyRouteDto[] | null | undefined): ProxyRoute[] =>
  Array.isArray(value)
    ? value
        .filter((route) => typeof route?.method === "string")
        .map((route) => ({
          method: route.method.toUpperCase() as ProxyMethod,
          path: trimSlashes(route.path),
          upstreamPath:
            typeof route.upstreamPath === "string" ? trimSlashes(route.upstreamPath) : null,
          headers: toRouteOverride(route.headers),
          query: toRouteOverride(route.query),
          bodyMerge: toRouteOverride(route.bodyMerge),
          responseMode:
            route.responseMode?.toLowerCase() === "select"
              ? "select"
              : route.responseMode?.toLowerCase() === "all"
                ? "all"
                : null,
          responseInclude: Array.isArray(route.responseInclude) ? [...route.responseInclude] : null,
          resilience: toResilience(route.resilience),
        }))
    : [];

/**
 * Reads the server's resilience object, keeping "not configured" distinct from "configured".
 *
 * Absent stays absent all the way through: substituting a number here would make the console show a
 * value the tenant never chose, and — because the console sends every field back — would then save
 * it. A setting that appears by being looked at is worse than one that is missing.
 */
const toResilience = (value: ProxyResilienceDto | null | undefined): ProxyResilience | null => {
  if (!value) return null;

  const retry =
    value.retry && typeof value.retry.attempts === "number"
      ? {
          attempts: value.retry.attempts,
          backoff:
            value.retry.backoff?.toLowerCase() === "fixed"
              ? ("fixed" as const)
              : value.retry.backoff?.toLowerCase() === "exponential"
                ? ("exponential" as const)
                : ("none" as const),
          initialDelaySeconds: value.retry.initialDelaySeconds ?? 1,
          idempotent: value.retry.idempotent === true,
        }
      : null;

  const breaker =
    value.breaker &&
    typeof value.breaker.failureThreshold === "number" &&
    typeof value.breaker.openSeconds === "number"
      ? {
          failureThreshold: value.breaker.failureThreshold,
          openSeconds: value.breaker.openSeconds,
        }
      : null;

  const timeoutSeconds = typeof value.timeoutSeconds === "number" ? value.timeoutSeconds : null;

  // An object that asks for nothing is the same as no object, which is how the server stores it too.
  return timeoutSeconds === null && retry === null && breaker === null
    ? null
    : { timeoutSeconds, retry, breaker };
};

/**
 * Sends resilience back exactly as it came. The console does not yet edit these, and the server
 * replaces the whole route on save — so omitting them here would silently delete a policy configured
 * through the API the first time somebody renamed a header.
 */
const toResilienceInput = (value: ProxyResilience | null): ProxyResilienceDto | null =>
  value
    ? {
        timeoutSeconds: value.timeoutSeconds,
        retry: value.retry
          ? {
              attempts: value.retry.attempts,
              backoff:
                value.retry.backoff === "fixed"
                  ? "Fixed"
                  : value.retry.backoff === "exponential"
                    ? "Exponential"
                    : "None",
              initialDelaySeconds: value.retry.initialDelaySeconds,
              idempotent: value.retry.idempotent,
            }
          : null,
        breaker: value.breaker ? { ...value.breaker } : null,
      }
    : null;

/**
 * Route payload. Sent on every create and update, including routes the form cannot edit: the server
 * replaces the whole list, so an omitted `routes` narrows the proxy to its base path.
 */
const toRouteInputs = (routes: ProxyRoute[] | undefined): ProxyRouteDto[] =>
  (routes ?? []).map((route) => ({
    method: route.method,
    path: trimSlashes(route.path),
    upstreamPath: route.upstreamPath ? trimSlashes(route.upstreamPath) : null,
    headers: route.headers ? compactKeyValues(route.headers) : null,
    query: route.query ? compactKeyValues(route.query) : null,
    bodyMerge: route.bodyMerge ? compactKeyValues(route.bodyMerge) : null,
    responseMode:
      route.responseMode === "select" ? "Select" : route.responseMode === "all" ? "All" : null,
    responseInclude: route.responseInclude
      ? route.responseInclude.map((path) => path.trim()).filter((path) => path.length > 0)
      : null,
    resilience: toResilienceInput(route.resilience ?? null),
  }));

export const mapProxyListItemDtoToProxy = (dto: ProxyListItemDto): Proxy => ({
  id: dto.itemId,
  name: dto.name,
  slug: dto.slug,
  upstreamUrl: dto.upstream ?? "",
  upstreamMasked: dto.upstreamMasked,
  methods: toMethods(dto.methods),
  enabled: dto.enabled,
  headers: [],
  query: [],
  bodyMerge: [],
  methodConfigs: [],
  routes: [],
  responseMode: "all",
  responseInclude: [],
  access: defaultProxyAccess(),
  // The list row does not carry it, and a placeholder here would read as "not configured" for a
  // proxy that is. Only the detail read can answer this.
  resilience: null,
  requestsPerMinute: null,
  version: null,
  calls24h: Number(dto.calls24h ?? 0),
  createdAt: dto.createdDate,
  updatedAt: dto.lastUpdatedDate,
});

/** `GET /api/Proxies/{proxyId}` — the full configuration for the form / detail view. */
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
  routes: toRoutes(dto.routes),
  responseMode: dto.responseMode?.toLowerCase() === "select" ? "select" : "all",
  responseInclude: Array.isArray(dto.responseInclude) ? dto.responseInclude : [],
  access: toAccess(dto.access),
  resilience: toResilience(dto.resilience),
  requestsPerMinute:
    typeof dto.requestsPerMinute === "number" && dto.requestsPerMinute > 0 ? dto.requestsPerMinute : null,
  version: typeof dto.currentVersion === "number" ? dto.currentVersion : null,
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

/** One row of `GET /api/Proxies/{proxyId}/versions`. `proxyId` comes from the request, not the DTO. */
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
 * One row of `GET /api/Proxies/{proxyId}/executions`. The list projection carries no upstream URL,
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
  responseContentType: undefined,
  outcome: dto.outcome ?? undefined,
});

/** `GET /api/Proxies/{proxyId}/executions/{executionId}` — the expanded row (no body: Blocks never stores one). */
export const mapProxyExecutionDetailDtoToLog = (
  dto: ProxyExecutionDetailDto,
): ProxyExecutionLog => ({
  id: dto.itemId,
  proxyId: dto.proxyId,
  timeUtc: dto.startedAtUtc,
  method: isMethod(dto.requestMethod) ? dto.requestMethod : "GET",
  path: dto.requestPath,
  requestQuery: dto.requestQuery || undefined,
  status: Number(dto.statusCode ?? 0),
  statusText: statusText(Number(dto.statusCode ?? 0)),
  latencyMs: Number(dto.latencyMs ?? 0),
  upstreamHost: dto.upstreamHost ?? "",
  upstreamUrl: dto.upstreamUrl ?? "",
  injectedHeaderKeys: Array.isArray(dto.injectedHeaderKeys) ? dto.injectedHeaderKeys : [],
  injectedQueryKeys: Array.isArray(dto.injectedQueryKeys) ? dto.injectedQueryKeys : [],
  responseContentType: dto.responseContentType ?? undefined,
  outcome: dto.outcome ?? undefined,
  errorMessage: dto.errorMessage ?? undefined,
});

/** `GET /api/Proxies/{proxyId}/overview` — the rolling 24h tiles for the detail view. */
export const mapProxyOverviewDtoToOverview = (dto: ProxyOverviewDto): ProxyOverview => ({
  calls24h: Number(dto.calls24h ?? 0),
  avgLatencyMs: Number(dto.avgLatencyMs ?? 0),
  errorRatePct: Number(dto.errorRatePct ?? 0),
  errorRateIsHigh: Boolean(dto.errorRateIsHigh),
  credentialRefs: Array.isArray(dto.credentialRefs) ? dto.credentialRefs : [],
  methods: toMethods(dto.methods),
  lastCallAtUtc: dto.lastCallAtUtc ?? null,
});

/** 200 body of `POST /api/Proxies/test` -> the shape the test panel renders. */
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

// ---------------------------------------------------------------------------
// OpenAPI import
// ---------------------------------------------------------------------------

const strings = (values: string[] | undefined): string[] =>
  Array.isArray(values) ? values.filter((value): value is string => typeof value === "string") : [];

/**
 * The preview, as the dialog reads it.
 *
 * An operation whose verb the gateway cannot forward is dropped rather than shown as unselectable:
 * the server already reports the skip as a warning, and listing a row nobody can import would make
 * the warning read like a bug. Everything else is carried across unchanged — in particular
 * `alreadyExists`, which the dialog then widens to include the routes in the unsaved form.
 */
export const mapOpenApiPreviewDtoToPreview = (dto: ProxyOpenApiPreviewDto): ProxyOpenApiPreview => ({
  baseUrl: dto.baseUrl ?? "",
  operations: (dto.operations ?? [])
    .map((operation) => ({
      operationId: operation.operationId ?? "",
      method: (operation.method ?? "").toUpperCase() as ProxyMethod,
      path: trimSlashes(operation.path ?? ""),
      summary: operation.summary ?? "",
      queryParameters: strings(operation.queryParameters),
      headerParameters: strings(operation.headerParameters),
      securityHeaders: strings(operation.securityHeaders),
      alreadyExists: operation.alreadyExists === true,
    }))
    .filter((operation) => PROXY_METHODS.includes(operation.method)),
  errors: strings(dto.errors),
  warnings: strings(dto.warnings),
});
