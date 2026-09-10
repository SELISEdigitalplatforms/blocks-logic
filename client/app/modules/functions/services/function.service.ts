import { serviceInstances } from "@/lib/http-client";
import { FUNCTIONS_ENDPOINTS } from "../constants/endpoint.constant";
import {
  IBaseResponse,
  ICreateFunctionPayload,
  IDeployFunctionPayload,
  IFunctionAuditEvent,
  IFunctionDetail,
  IFunctionLimitsOptions,
  IGetFunctionsPayload,
  IGetFunctionsResponse,
  IRollbackFunctionPayload,
  ISaveFunctionPayload,
  ISecretCatalogEntry,
  IUpdateFunctionPayload,
} from "../types/function.types";
import {
  IFunctionBuild,
  IFunctionVersionSummary,
  IGetVersionsResponse,
} from "../types/version.types";
import {
  IGetRunLogsResponse,
  IGetRunsPayload,
  IGetRunsResponse,
  IInvokeResult,
  IRunDetail,
  ITestFunctionPayload,
} from "../types/run.types";
import { IFunctionSource } from "../types/function.types";

export interface IFunctionVersionSummaryResponse {
  data: IFunctionVersionSummary[] | null;
  totalCount: number;
}

export interface IFunctionAuditLogResponse {
  data: IFunctionAuditEvent[] | null;
  totalCount: number;
}

export class FunctionService {
  private readonly logicHttpClient = serviceInstances.logicService;

  getFunctions = (payload: IGetFunctionsPayload): Promise<IGetFunctionsResponse> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.GET_ALL, payload);
  };

  getFunction = (functionId: string): Promise<IFunctionDetail> => {
    const params = new URLSearchParams({ functionId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET}?${params.toString()}`);
  };

  getLimitsOptions = (): Promise<IFunctionLimitsOptions> => {
    return this.logicHttpClient.get(FUNCTIONS_ENDPOINTS.GET_LIMITS);
  };

  createFunction = (payload: ICreateFunctionPayload): Promise<IFunctionDetail> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.CREATE, payload);
  };

  updateFunction = (payload: IUpdateFunctionPayload): Promise<IFunctionDetail> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.UPDATE, payload);
  };

  saveFunction = (payload: ISaveFunctionPayload): Promise<IFunctionDetail> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.SAVE, payload);
  };

  deleteFunction = (functionId: string): Promise<IBaseResponse> => {
    const params = new URLSearchParams({ functionId });
    return this.logicHttpClient.delete(`${FUNCTIONS_ENDPOINTS.DELETE}?${params.toString()}`);
  };

  testFunction = (payload: ITestFunctionPayload): Promise<IInvokeResult> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.TEST, payload);
  };

  deployFunction = (payload: IDeployFunctionPayload): Promise<IFunctionVersionSummary> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.DEPLOY, payload);
  };

  rollbackFunction = (payload: IRollbackFunctionPayload): Promise<IFunctionVersionSummary> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.ROLLBACK, payload);
  };

  getVersions = (
    functionId: string,
    pageNumber = 0,
    pageSize = 20,
  ): Promise<IGetVersionsResponse> => {
    const params = new URLSearchParams({
      functionId,
      pageNumber: String(pageNumber),
      pageSize: String(pageSize),
    });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_VERSIONS}?${params.toString()}`);
  };

  getVersionSource = (functionId: string, versionId: string): Promise<IFunctionSource> => {
    const params = new URLSearchParams({ functionId, versionId });
    return this.logicHttpClient.get(
      `${FUNCTIONS_ENDPOINTS.GET_VERSION_SOURCE}?${params.toString()}`,
    );
  };

  getBuild = (buildId: string): Promise<IFunctionBuild> => {
    const params = new URLSearchParams({ buildId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_BUILD}?${params.toString()}`);
  };

  getRuns = (payload: IGetRunsPayload): Promise<IGetRunsResponse> => {
    const params = new URLSearchParams();
    if (payload.functionId) params.set("functionId", payload.functionId);
    if (payload.status) params.set("status", payload.status);
    if (payload.fromUtc) params.set("fromUtc", payload.fromUtc);
    if (payload.toUtc) params.set("toUtc", payload.toUtc);
    params.set("pageNumber", String(payload.pageNumber));
    params.set("pageSize", String(payload.pageSize));
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_RUNS}?${params.toString()}`);
  };

  getRun = (runId: string): Promise<IRunDetail> => {
    const params = new URLSearchParams({ runId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_RUN}?${params.toString()}`);
  };

  getRunLogs = (
    runId: string,
    pageNumber = 0,
    pageSize = 200,
  ): Promise<IGetRunLogsResponse> => {
    const params = new URLSearchParams({
      runId,
      pageNumber: String(pageNumber),
      pageSize: String(pageSize),
    });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_RUN_LOGS}?${params.toString()}`);
  };

  replayRun = (runId: string): Promise<IInvokeResult> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.REPLAY_RUN, { runId });
  };

  cancelRun = (runId: string): Promise<IBaseResponse> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.CANCEL_RUN, { runId });
  };

  getSecretCatalog = (): Promise<ISecretCatalogEntry[]> => {
    return this.logicHttpClient.get(FUNCTIONS_ENDPOINTS.GET_SECRET_CATALOG);
  };

  getAuditLog = (
    functionId: string,
    pageNumber = 0,
    pageSize = 50,
  ): Promise<IFunctionAuditLogResponse> => {
    const params = new URLSearchParams({
      functionId,
      pageNumber: String(pageNumber),
      pageSize: String(pageSize),
    });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_AUDIT_LOG}?${params.toString()}`);
  };
}

export const functionService = new FunctionService();
