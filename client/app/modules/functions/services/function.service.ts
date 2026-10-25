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
  ISaveFunctionPayload,
  IUpdateFunctionPayload,
} from "../types/function.types";
import {
  IFunctionBuild,
  IPendingDeploy,
  IFunctionVersionSummary,
  IDeployPending,
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
import {
  IEventCatalog,
  IEventSettings,
  IQueueStatus,
  IRunEventsResponse,
  ISaveEventSettingsPayload,
} from "../types/event.types";
import {
  IFunctionExportFile,
  IFunctionImportApplyPayload,
  IFunctionImportPreview,
  IFunctionImportResult,
} from "../types/transfer.types";

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

  /** `force`: delete even though runs have not finished (they are cancelled) or workflows still use it. */
  deleteFunction = (functionId: string, force = false): Promise<IBaseResponse> => {
    const params = new URLSearchParams({ functionId });
    if (force) params.set("force", "true");
    return this.logicHttpClient.delete(`${FUNCTIONS_ENDPOINTS.DELETE}?${params.toString()}`);
  };

  testFunction = (payload: ITestFunctionPayload): Promise<IInvokeResult> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.TEST, payload);
  };

  /**
   * A run's streamed answer as Server-Sent Events (see `readSse`). A run that finished without
   * streaming answers an empty stream.
   */
  streamRun = (runId: string, signal: AbortSignal): Promise<ReadableStream<Uint8Array>> => {
    return this.logicHttpClient.stream(
      FUNCTIONS_ENDPOINTS.STREAM_RUN,
      { runId },
      { Accept: "text/event-stream" },
      { signal },
    );
  };

  /** 200 → the new version; 202 → the build is still running (see useDeploy). */
  deployFunction = (
    payload: IDeployFunctionPayload,
  ): Promise<IFunctionVersionSummary | IDeployPending> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.DEPLOY, payload);
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

  getPendingDeploy = (functionId: string): Promise<IPendingDeploy> => {
    const params = new URLSearchParams({ functionId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_PENDING_DEPLOY}?${params.toString()}`);
  };

  getRuns = (payload: IGetRunsPayload): Promise<IGetRunsResponse> => {
    const params = new URLSearchParams();
    if (payload.functionId) params.set("functionId", payload.functionId);
    if (payload.status) params.set("status", payload.status);
    if (payload.invokedBy) params.set("invokedBy", payload.invokedBy);
    if (payload.searchKey) params.set("searchKey", payload.searchKey);
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

  getRunLogs = (runId: string, pageNumber = 0, pageSize = 200): Promise<IGetRunLogsResponse> => {
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

  /** One function as an export file: secrets by name, nothing environment-specific. */
  exportFunction = (functionId: string): Promise<IFunctionExportFile> => {
    const params = new URLSearchParams({ functionId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.EXPORT}?${params.toString()}`);
  };

  /** What an import of `file` would do here. Writes nothing. */
  previewImport = (file: IFunctionExportFile): Promise<IFunctionImportPreview> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.IMPORT_PREVIEW, { file });
  };

  /** Imports `file` (409 when the function changed after the preview), then builds and deploys it. */
  applyImport = (payload: IFunctionImportApplyPayload): Promise<IFunctionImportResult> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.IMPORT, payload);
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

  /** The event types a function may send, each with the payload to start from. */
  getEventCatalog = (): Promise<IEventCatalog> => {
    return this.logicHttpClient.get(FUNCTIONS_ENDPOINTS.GET_EVENT_CATALOG);
  };

  /** The draft's events and trigger events. */
  getEventSettings = (functionId: string): Promise<IEventSettings> => {
    const params = new URLSearchParams({ functionId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_EVENT_SETTINGS}?${params.toString()}`);
  };

  /** Replaces the draft's whole event settings. 400 `invalid_request` lists each bad field by path. */
  saveEventSettings = (payload: ISaveEventSettingsPayload): Promise<IBaseResponse> => {
    return this.logicHttpClient.post(FUNCTIONS_ENDPOINTS.SAVE_EVENT_SETTINGS, payload);
  };

  /** The events one run sent, and why they were dropped when they were. */
  getRunEvents = (runId: string): Promise<IRunEventsResponse> => {
    const params = new URLSearchParams({ runId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_RUN_EVENTS}?${params.toString()}`);
  };

  /** How many calls wait for a sandbox slot: HTTP ones and event ones. */
  getQueueStatus = (functionId: string): Promise<IQueueStatus> => {
    const params = new URLSearchParams({ functionId });
    return this.logicHttpClient.get(`${FUNCTIONS_ENDPOINTS.GET_QUEUE_STATUS}?${params.toString()}`);
  };
}

export const functionService = new FunctionService();
