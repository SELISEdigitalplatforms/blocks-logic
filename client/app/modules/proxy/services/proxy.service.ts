import { HttpError } from "@seliseblocks/genesis-os";
import { serviceInstances } from "@/lib/http-client";
import { PROXY_ENDPOINTS, PROXY_LOG_PAGE_SIZE } from "../constants";
import {
  mapLogFilterToStatusClass,
  mapMutationResponse,
  mapProxyDetailDtoToProxy,
  mapProxyExecutionDetailDtoToLog,
  mapProxyExecutionListItemDtoToLog,
  mapProxyListItemDtoToProxy,
  mapOpenApiPreviewDtoToPreview,
  mapProxyOverviewDtoToOverview,
  mapProxyTestResponseDtoToResponse,
  mapProxyToCreatePayload,
  mapProxyToUpdatePayload,
  mapProxyTestRequestToPayload,
  mapProxyVersionDtoToHistory,
} from "../mappers";
import {
  BaseMutationResponseDto,
  BaseQueryListResponse,
  BaseQueryResponse,
  Proxy,
  ProxyDetailDto,
  ProxyExecutionDetailDto,
  ProxyExecutionListItemDto,
  ProxyExecutionLog,
  ProxyExecutionPage,
  ProxyFormValues,
  ProxyListItemDto,
  ProxyListPage,
  ProxyListParams,
  ProxyLogFilter,
  ProxyMutationResponse,
  ProxyOpenApiPreview,
  ProxyOpenApiPreviewDto,
  ProxyOpenApiPreviewRequest,
  ProxyOverview,
  ProxyOverviewDto,
  ProxyTestRequest,
  ProxyTestResponse,
  ProxyTestResponseDto,
  ProxyVersionDto,
  ProxyVersionHistory,
} from "../types";

/** Turn a thrown {@link HttpError} into the `{ isSuccess: false, ... }` shape callers already branch on. */
const toMutationFailure = (error: unknown): ProxyMutationResponse => {
  if (error instanceof HttpError) {
    const body = (error.errors ?? {}) as Record<string, unknown>;
    const isEnvelope = body && typeof body === "object" && "isSuccess" in body;
    const message = typeof body.message === "string" ? body.message : undefined;
    const code = typeof body.code === "string" ? body.code : null;
    const fieldErrors = !isEnvelope
      ? (body as Record<string, string>)
      : ((body.errors as Record<string, string> | null) ?? undefined);

    return {
      isSuccess: false,
      errors: message ?? fieldErrors ?? "The request could not be completed.",
      code,
      message: message ?? null,
    };
  }

  return { isSuccess: false, errors: "The request could not be completed.", code: null };
};

/**
 * The reasons and warnings off a refused preview. The endpoint answers with a list of sentences
 * rather than the field map a save returns, and an older or proxied error may be neither — hence the
 * fallback, which still says something true.
 */
const toPreviewFailure = (error: unknown): { errors: string[]; warnings: string[] } => {
  if (error instanceof HttpError) {
    const body = (error.errors ?? {}) as Record<string, unknown>;
    const list = (value: unknown): string[] =>
      Array.isArray(value)
        ? value.filter((entry): entry is string => typeof entry === "string")
        : value && typeof value === "object"
          ? Object.values(value as Record<string, unknown>).filter(
              (entry): entry is string => typeof entry === "string",
            )
          : typeof value === "string"
            ? [value]
            : [];

    const errors = list(body.errors);
    if (errors.length) return { errors, warnings: list(body.warnings) };

    const message = typeof body.message === "string" ? body.message : null;
    if (message) return { errors: [message], warnings: list(body.warnings) };
  }

  return { errors: ["The specification could not be read."], warnings: [] };
};

const flattenErrors = (error: unknown): string => {
  if (error instanceof HttpError) {
    const body = (error.errors ?? {}) as Record<string, unknown>;
    if (typeof body.message === "string") return body.message;
    const values = Object.values(body).filter(
      (value): value is string => typeof value === "string",
    );
    if (values.length) return values.join(" ");
  }
  return "The proxy test request could not be completed.";
};

const buildQuery = (params: Record<string, string | number | boolean | undefined>) => {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") {
      search.append(key, String(value));
    }
  });
  const query = search.toString();
  return query ? `?${query}` : "";
};

export class ProxyService {
  private readonly logicHttpClient = serviceInstances.logicService;

  endpoints = PROXY_ENDPOINTS;

  getAll = async (params: ProxyListParams = {}): Promise<ProxyListPage> => {
    const response = await this.logicHttpClient.get<BaseQueryListResponse<ProxyListItemDto[]>>(
      `${PROXY_ENDPOINTS.COLLECTION}${buildQuery({
        search: params.search?.trim() || undefined,
        isActive: params.isActive,
        pageSize: params.pageSize ?? 200,
        pageNumber: params.pageNumber ?? 0,
      })}`,
    );
    const items = (response.data ?? []).map(mapProxyListItemDtoToProxy);
    return { items, totalCount: response.totalCount ?? items.length };
  };

  get = async (id: string): Promise<Proxy | null> => {
    const response = await this.logicHttpClient.get<BaseQueryResponse<ProxyDetailDto | null>>(
      PROXY_ENDPOINTS.byId(id),
    );
    return response.data ? mapProxyDetailDtoToProxy(response.data) : null;
  };

  create = async (values: ProxyFormValues): Promise<ProxyMutationResponse> => {
    try {
      const response = await this.logicHttpClient.post<BaseMutationResponseDto>(
        PROXY_ENDPOINTS.COLLECTION,
        mapProxyToCreatePayload(values),
      );
      return mapMutationResponse(response);
    } catch (error) {
      return toMutationFailure(error);
    }
  };

  update = async ({
    id,
    values,
    expectedVersion = null,
  }: {
    id: string;
    values: ProxyFormValues;
    expectedVersion?: number | null;
  }): Promise<ProxyMutationResponse> => {
    try {
      const response = await this.logicHttpClient.put<BaseMutationResponseDto>(
        PROXY_ENDPOINTS.byId(id),
        mapProxyToUpdatePayload(id, values, expectedVersion),
      );
      return mapMutationResponse(response);
    } catch (error) {
      return toMutationFailure(error);
    }
  };

  toggle = async ({
    id,
    enabled,
    expectedVersion = null,
  }: {
    id: string;
    enabled: boolean;
    expectedVersion?: number | null;
  }): Promise<ProxyMutationResponse> => {
    try {
      const response = await this.logicHttpClient.patch<BaseMutationResponseDto>(
        PROXY_ENDPOINTS.byId(id),
        { enabled, expectedVersion },
      );
      return mapMutationResponse(response);
    } catch (error) {
      return toMutationFailure(error);
    }
  };

  delete = async (id: string): Promise<ProxyMutationResponse> => {
    try {
      const response = await this.logicHttpClient.delete<BaseMutationResponseDto>(
        PROXY_ENDPOINTS.byId(id),
      );
      return mapMutationResponse(response);
    } catch (error) {
      return toMutationFailure(error);
    }
  };

  getExecutions = async (
    proxyId: string,
    filter: ProxyLogFilter,
    options: {
      live?: boolean;
      afterId?: string;
      page?: number;
      pageSize?: number;
      asOfUtc?: string;
    } = {},
  ): Promise<ProxyExecutionPage> => {
    const response = await this.logicHttpClient.get<
      BaseQueryListResponse<ProxyExecutionListItemDto[]> & { asOfUtc?: string }
    >(
      `${PROXY_ENDPOINTS.executions(proxyId)}${buildQuery({
        statusClass: mapLogFilterToStatusClass(filter),
        afterId: options.afterId,
        pageSize: options.pageSize ?? PROXY_LOG_PAGE_SIZE,
        pageNumber: options.page ?? 0,
        asOfUtc: options.asOfUtc,
      })}`,
    );
    return {
      rows: (response.data ?? []).map((row) => mapProxyExecutionListItemDtoToLog(row, proxyId)),
      totalCount: response.totalCount ?? 0,
      asOfUtc: response.asOfUtc,
    };
  };

  getExecution = async (
    proxyId: string,
    executionId: string,
  ): Promise<ProxyExecutionLog | null> => {
    const response = await this.logicHttpClient.get<
      BaseQueryResponse<ProxyExecutionDetailDto | null>
    >(PROXY_ENDPOINTS.execution(proxyId, executionId));
    return response.data ? mapProxyExecutionDetailDtoToLog(response.data) : null;
  };

  getOverview = async (proxyId: string): Promise<ProxyOverview | null> => {
    const response = await this.logicHttpClient.get<BaseQueryResponse<ProxyOverviewDto | null>>(
      PROXY_ENDPOINTS.overview(proxyId),
    );
    return response.data ? mapProxyOverviewDtoToOverview(response.data) : null;
  };

  getVersions = async (proxyId: string): Promise<ProxyVersionHistory[]> => {
    const response = await this.logicHttpClient.get<BaseQueryListResponse<ProxyVersionDto[]>>(
      `${PROXY_ENDPOINTS.versions(proxyId)}${buildQuery({ pageSize: 200, pageNumber: 0 })}`,
    );
    return (response.data ?? []).map((row) => mapProxyVersionDtoToHistory(row, proxyId));
  };

  revert = async ({
    proxyId,
    versionId,
  }: {
    proxyId: string;
    versionId: string;
  }): Promise<ProxyMutationResponse> => {
    try {
      const response = await this.logicHttpClient.post<BaseMutationResponseDto>(
        PROXY_ENDPOINTS.revert(proxyId, versionId),
        null,
      );
      return mapMutationResponse(response);
    } catch (error) {
      return toMutationFailure(error);
    }
  };

  /**
   * Reads an OpenAPI document and reports what it would produce. Nothing is written; the operations
   * the user picks are added to the form and saved through the ordinary create / update call, which
   * is what keeps an import inside the same validation, versioning and audit as a hand-typed route.
   *
   * A document the server could not use comes back as a 400 whose body carries the reasons. Those are
   * the answer to "why did my paste produce nothing", so they are returned as a preview with errors
   * rather than thrown — the dialog has somewhere to show them.
   */
  previewOpenApi = async (request: ProxyOpenApiPreviewRequest): Promise<ProxyOpenApiPreview> => {
    try {
      const response = await this.logicHttpClient.post<ProxyOpenApiPreviewDto>(
        PROXY_ENDPOINTS.OPENAPI_PREVIEW,
        {
          specJson: request.specJson ?? null,
          specUrl: request.specUrl ?? null,
          proxyId: request.proxyId ?? null,
        },
      );
      return mapOpenApiPreviewDtoToPreview(response);
    } catch (error) {
      return { baseUrl: "", operations: [], ...toPreviewFailure(error) };
    }
  };

  test = async (request: ProxyTestRequest): Promise<ProxyTestResponse> => {
    try {
      const response = await this.logicHttpClient.post<ProxyTestResponseDto>(
        PROXY_ENDPOINTS.TEST,
        mapProxyTestRequestToPayload(request),
      );
      return mapProxyTestResponseDtoToResponse(response, request);
    } catch (error) {
      const status = error instanceof HttpError ? error.status : 0;
      return {
        ok: false,
        status,
        statusText: status === 400 ? "Bad Request" : "Request Failed",
        latencyMs: 0,
        meta: flattenErrors(error),
        responseBody: "",
        responseBodyBytes: 0,
      };
    }
  };
}

export const proxyService = new ProxyService();
