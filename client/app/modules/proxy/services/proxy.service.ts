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
        search: params.searchKey?.trim() || undefined,
        enabled: params.enabled,
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
  }: {
    id: string;
    values: ProxyFormValues;
  }): Promise<ProxyMutationResponse> => {
    try {
      const response = await this.logicHttpClient.put<BaseMutationResponseDto>(
        PROXY_ENDPOINTS.byId(id),
        mapProxyToUpdatePayload(id, values),
      );
      return mapMutationResponse(response);
    } catch (error) {
      return toMutationFailure(error);
    }
  };

  toggle = async ({
    id,
    enabled,
  }: {
    id: string;
    enabled: boolean;
  }): Promise<ProxyMutationResponse> => {
    try {
      const response = await this.logicHttpClient.patch<BaseMutationResponseDto>(
        PROXY_ENDPOINTS.byId(id),
        { enabled },
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
