import type { IProject } from "@seliseblocks/genesis-os";
import { API_BASES } from "@/constants/endpoint.constant";
import {
  PUBLIC_GATEWAY_PREFIX,
  getProxyPublicHost,
} from "@/modules/proxy/constants/proxy.constant";
import type { ProxyMethod } from "@/modules/proxy/types";
import type { HttpTriggerMethod } from "../types/function.types";

const FUNCTIONS_SUBPATH = "/Functions";

export const FUNCTIONS_ENDPOINTS = {
  CREATE: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Create`,
  UPDATE: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Update`,
  SAVE: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Save`,
  DELETE: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Delete`,
  GET_ALL: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetAll`,
  GET: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Get`,
  GET_LIMITS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetLimits`,
  TEST: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Test`,
  DEPLOY: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Deploy`,
  ROLLBACK: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Rollback`,
  GET_VERSIONS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetVersions`,
  GET_VERSION_SOURCE: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetVersionSource`,
  GET_BUILD: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetBuild`,
  GET_RUNS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetRuns`,
  GET_RUN: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetRun`,
  GET_RUN_LOGS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetRunLogs`,
  REPLAY_RUN: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/ReplayRun`,
  CANCEL_RUN: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/CancelRun`,
  GET_AUDIT_LOG: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetAuditLog`,
} as const;

/**
 * The methods a trigger can pick from. The public route (`FunctionsController.Invoke`) is
 * registered for exactly these two; the trigger chooses one, and a call with the other is refused
 * with 405. Server enum names, so the value round-trips into `ITriggerConfig.httpMethod` as is.
 */
export const FUNCTION_HTTP_METHODS: HttpTriggerMethod[] = ["Get", "Post"];

/** The trigger's method as the wire verb the badges and the snippet show. */
export const toHttpVerb = (method: HttpTriggerMethod): ProxyMethod =>
  method === "Get" ? "GET" : "POST";

/**
 * The data-plane path a tenant's client calls: `{METHOD} /logic/v4/fn/{id}/{**path}` on the public
 * API gateway, which addresses the service's own `~/api/fn/{id}/{**path}` under that prefix so the
 * `/api` segment never appears in a URL handed to a tenant — exactly how proxy URLs are published.
 * Pinned server-side: it is a published contract, not a convention-derived URL.
 */
export const getFunctionClientPath = (functionId: string, routePath?: string) => {
  const root = `${PUBLIC_GATEWAY_PREFIX}/fn/${functionId || "{id}"}`;
  const suffix = (routePath ?? "").replace(/^\/+|\/+$/g, "");
  return suffix ? `${root}/${suffix}` : root;
};

/**
 * Full customer-facing URL for a function, or just the path when no public host is configured
 * yet. Same host rule as a proxy (`getProxyPublicHost`): the project's own `blocksapi.<domain>`
 * once it has one, else the shared public API host — never the console's own base URL.
 */
export const getFunctionClientUrl = (
  project: IProject | null | undefined,
  functionId: string,
  routePath?: string,
) => `${getProxyPublicHost(project)}${getFunctionClientPath(functionId, routePath)}`;
