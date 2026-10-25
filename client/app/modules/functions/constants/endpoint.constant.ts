import type { IProject } from "@seliseblocks/genesis-os";
import { API_BASES } from "@/constants/endpoint.constant";
import {
  PUBLIC_GATEWAY_PREFIX,
  customDomainRoot,
  getProxyPublicHost,
} from "@/modules/proxy/constants/proxy.constant";
import type { ProxyMethod } from "@/modules/proxy/types";
import type { HttpTriggerMethod, HttpTriggerVerb, ITriggerConfig } from "../types/function.types";

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
  GET_VERSIONS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetVersions`,
  GET_VERSION_SOURCE: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetVersionSource`,
  GET_BUILD: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetBuild`,
  GET_PENDING_DEPLOY: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetPendingDeploy`,
  GET_RUNS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetRuns`,
  GET_RUN: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetRun`,
  GET_RUN_LOGS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetRunLogs`,
  STREAM_RUN: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/StreamRun`,
  REPLAY_RUN: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/ReplayRun`,
  CANCEL_RUN: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/CancelRun`,
  GET_AUDIT_LOG: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetAuditLog`,
  EXPORT: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Export`,
  IMPORT_PREVIEW: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/ImportPreview`,
  IMPORT: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/Import`,
  GET_EVENT_CATALOG: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetEventCatalog`,
  GET_EVENT_SETTINGS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetEventSettings`,
  SAVE_EVENT_SETTINGS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/SaveEventSettings`,
  GET_RUN_EVENTS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetRunEvents`,
  GET_QUEUE_STATUS: `${API_BASES.WORKFLOW}${FUNCTIONS_SUBPATH}/GetQueueStatus`,
} as const;

/** The trigger's method as the wire verb the badges and the snippet show. */
export const toHttpVerb = (method: HttpTriggerMethod): ProxyMethod =>
  method === "Get" ? "GET" : "POST";

/**
 * Every verb the public route accepts once a trigger lists more than one (`httpMethods`). The
 * route answers all five and refuses the ones the trigger does not list with 405 + `Allow`.
 */
export const FUNCTION_HTTP_VERBS: HttpTriggerVerb[] = ["GET", "POST", "PUT", "PATCH", "DELETE"];

/**
 * The verbs a trigger actually accepts, in the fixed GET → DELETE order: its `httpMethods` when it
 * has any, else the single legacy `httpMethod` — what every function saved before the list existed
 * still answers.
 */
export const acceptedHttpVerbs = (
  trigger: Pick<ITriggerConfig, "httpMethod" | "httpMethods">,
): ProxyMethod[] => {
  const listed = trigger.httpMethods ?? [];
  if (listed.length === 0) return [toHttpVerb(trigger.httpMethod)];
  return FUNCTION_HTTP_VERBS.filter((verb) => listed.includes(verb));
};

/**
 * The verb a Test run is sent with — the client twin of the server's
 * `FunctionHttpInputBuilder.TestVerb`: the legacy method when the list still contains it,
 * otherwise the first accepted verb. Decides whether test input arrives as input.query (GET) or
 * input.body (everything else).
 */
export const testHttpVerb = (
  trigger: Pick<ITriggerConfig, "httpMethod" | "httpMethods">,
): ProxyMethod => {
  const accepted = acceptedHttpVerbs(trigger);
  const legacy = toHttpVerb(trigger.httpMethod);
  return accepted.includes(legacy) ? legacy : accepted[0];
};

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

/**
 * The value a caller sends as `x-blocks-key`: the selected environment's tenant id, which is
 * what the platform resolves the tenant from. Only an id-shaped value is inlined — anything else
 * (no environment selected yet, or a value that would break out of the quoted header) falls back
 * to the placeholder rather than producing a command that silently targets the wrong tenant.
 */
export const snippetBlocksKey = (tenantId?: string | null) => {
  const key = tenantId?.trim() ?? "";
  return /^[A-Za-z0-9_-]+$/.test(key) ? key : "<your x-blocks-key>";
};

/**
 * `https://blocksapi.<root domain>` for the project's custom domain (`app.acme.com` → `acme.com`,
 * stored bare or as a URL), or `null` when the project has no usable one. Unlike
 * {@link getFunctionClientUrl} it never falls back to the shared host: the snippet shows a
 * placeholder instead, so a caller is not sent to a host that is not theirs.
 */
export const getFunctionBaseUrl = (project?: IProject | null): string | null => {
  const root = customDomainRoot(project?.customDomain);
  return root ? `https://blocksapi.${root}` : null;
};

/** Shown in place of the base URL until the project has a custom domain. */
export const FUNCTION_BASE_URL_PLACEHOLDER = "https://blocksapi.<your-domain>";

/** The anonymous poll route: `GET {prefix}/fn/runs/{runId}/result` (needs `x-poll-token`). */
export const getFunctionPollPath = (runId: string) =>
  `${PUBLIC_GATEWAY_PREFIX}/fn/runs/${runId}/result`;
