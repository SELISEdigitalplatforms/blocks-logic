import { maskUpstreamUrl, slugifyProxyName } from "../utils";
import {
  Proxy,
  ProxyBackendDto,
  ProxyExecutionLog,
  ProxyFormValues,
  ProxyKeyValue,
  ProxyMethod,
  ProxyVersionHistory,
} from "../types";

const isMethod = (value: unknown): value is ProxyMethod =>
  typeof value === "string" && ["GET", "POST", "PUT", "PATCH", "DELETE"].includes(value);

const toKeyValues = (value: unknown): ProxyKeyValue[] => {
  if (Array.isArray(value)) {
    return value
      .filter((item): item is Record<string, unknown> => !!item && typeof item === "object")
      .map((item) => ({
        key: String(item.key ?? ""),
        value: String(item.value ?? ""),
        isSecretRef: Boolean(item.isSecretRef),
      }));
  }

  if (value && typeof value === "object") {
    return Object.entries(value).map(([key, itemValue]) => ({
      key,
      value: String(itemValue ?? ""),
      isSecretRef: String(itemValue ?? "").startsWith("${SECRET."),
    }));
  }

  return [];
};

export const mapProxyDtoToProxy = (dto: ProxyBackendDto): Proxy => {
  const name = String(dto.name ?? "Untitled proxy");
  const upstreamUrl = String(dto.upstreamUrl ?? dto.upstream ?? "");
  const enabled = dto.enabled === undefined ? true : Boolean(dto.enabled);
  const methods: ProxyMethod[] = Array.isArray(dto.methods)
    ? dto.methods.filter(isMethod)
    : ["GET"];

  return {
    id: String(dto.itemId ?? dto.id ?? ""),
    name,
    slug: String(dto.slug ?? dto.path ?? slugifyProxyName(name)),
    upstreamUrl,
    upstreamMasked: String(dto.upstreamMasked ?? maskUpstreamUrl(upstreamUrl)),
    methods: methods.length ? methods : ["GET"],
    enabled,
    headers: toKeyValues(dto.headers),
    query: toKeyValues(dto.query),
    calls24h: Number(dto.calls24h ?? 0),
    createdAt: typeof dto.createdDate === "string" ? dto.createdDate : undefined,
    updatedAt: typeof dto.lastUpdatedDate === "string" ? dto.lastUpdatedDate : undefined,
  };
};

export const mapProxyToCreatePayload = (values: ProxyFormValues): Record<string, unknown> => ({
  name: values.name.trim(),
  slug: slugifyProxyName(values.name),
  upstream: values.upstreamUrl.trim(),
  methods: values.methods,
  enabled: true,
  headers: values.headers,
  query: values.query,
});

export const mapProxyToUpdatePayload = (
  id: string,
  values: ProxyFormValues,
): Record<string, unknown> => ({
  itemId: id,
  ...mapProxyToCreatePayload(values),
});

export const mapProxyExecutionDtoToLog = (dto: ProxyBackendDto): ProxyExecutionLog => ({
  id: String(dto.id ?? dto.itemId ?? ""),
  proxyId: String(dto.proxyId ?? dto.proxyItemId ?? ""),
  timeUtc: String(dto.timeUtc ?? dto.createdDate ?? new Date().toISOString()),
  method: isMethod(dto.method) ? dto.method : "GET",
  path: String(dto.path ?? dto.clientPath ?? ""),
  status: Number(dto.status ?? dto.statusCode ?? 0),
  statusText: String(dto.statusText ?? dto.reasonPhrase ?? ""),
  latencyMs: Number(dto.latencyMs ?? dto.durationMs ?? 0),
  upstreamHost: String(dto.upstreamHost ?? dto.host ?? ""),
  upstreamUrl: String(dto.upstreamUrl ?? dto.upstream ?? ""),
  injectedHeaderKeys: Array.isArray(dto.injectedHeaderKeys) ? dto.injectedHeaderKeys.map(String) : [],
  injectedQueryKeys: Array.isArray(dto.injectedQueryKeys) ? dto.injectedQueryKeys.map(String) : [],
  responseBody: String(dto.responseBody ?? dto.body ?? ""),
  responseContentType:
    typeof dto.responseContentType === "string" ? dto.responseContentType : undefined,
});

export const mapProxyVersionDtoToHistory = (dto: ProxyBackendDto): ProxyVersionHistory => ({
  id: String(dto.id ?? dto.itemId ?? ""),
  proxyId: String(dto.proxyId ?? dto.proxyItemId ?? ""),
  versionLabel: String(dto.versionLabel ?? dto.label ?? "v1"),
  versionNumber: Number(dto.versionNumber ?? dto.version ?? 1),
  kind: ["create", "edit", "toggle", "revert", "delete"].includes(String(dto.kind))
    ? (dto.kind as ProxyVersionHistory["kind"])
    : "edit",
  summary: String(dto.summary ?? ""),
  actor: String(dto.actor ?? dto.createdBy ?? "Unknown"),
  whenUtc: String(dto.whenUtc ?? dto.createdDate ?? new Date().toISOString()),
  before: typeof dto.before === "string" || dto.before === null ? dto.before : undefined,
  after: typeof dto.after === "string" || dto.after === null ? dto.after : undefined,
});
