export type FunctionStatus = "Draft" | "Live" | "Paused";

export type AuthMode = "Public" | "Token";
export type MatchMode = "Any" | "All";
export type BackoffKind = "None" | "Fixed" | "Exponential";
export type OutputActionKind = "ExternalHttp" | "BlocksProxy";

export interface IFunctionLimits {
  cpuMillicores: number;
  memoryMb: number;
  timeoutSeconds: number;
  concurrency: number;
  requestsPerMinute?: number | null;
  requestsPerDay?: number | null;
}

export interface IRetryPolicy {
  attempts: number;
  backoff: BackoffKind;
  initialDelaySeconds: number;
  maxDelaySeconds: number;
}

/** OR / AND between the roles rule and the permissions rule once both are configured. */
export type AccessCombine = "Or" | "And";

/** The one method the endpoint answers — server enum names, like the other trigger enums. */
export type HttpTriggerMethod = "Get" | "Post";

/** A verb the trigger may accept when it takes more than one — wire spelling, upper case. */
export type HttpTriggerVerb = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

/**
 * How an HTTP call is answered: `async` returns 202 + poll token straight away (the default);
 * `sync` waits for the function's answer and returns it, falling back to 202 + poll token when
 * the run takes longer than the wait limit.
 */
export type TriggerResponseMode = "async" | "sync";

export interface ITriggerConfig {
  httpEnabled: boolean;
  /** GET or POST, one per function; the other method is refused with 405. Used when
   *  {@link ITriggerConfig.httpMethods} is empty — every function saved before that list existed. */
  httpMethod: HttpTriggerMethod;
  /** The verbs the endpoint accepts. Empty → only the single {@link ITriggerConfig.httpMethod}. */
  httpMethods: HttpTriggerVerb[];
  /** Keep the sandbox loaded between calls (warm). Default false. */
  reuseSandbox: boolean;
  /** Default `async`. */
  responseMode: TriggerResponseMode;
  authMode: AuthMode;
  roles: string[];
  permissions: string[];
  /** any / all within the roles list. */
  roleMatch: MatchMode;
  /** any / all within the permissions list. */
  permissionMatch: MatchMode;
  /** How the two lists combine — the one toggle "Restrict further" shows, as on a proxy. */
  combine: AccessCombine;
  workflowEnabled: boolean;
}

export interface IOutputAction {
  id: string;
  kind: OutputActionKind;
  enabled: boolean;
  url: string;
  method: string;
  headers: Record<string, string>;
  bodyTemplate?: string | null;
  timeoutSeconds: number;
}

export interface IVariableBinding {
  key: string;
  value: string;
}

export interface IFunctionSource {
  indexJs: string;
  packageJson: string;
  lockJson?: string | null;
}

export interface IFunctionSummary {
  id: string;
  name: string;
  status: FunctionStatus;
  isDirty: boolean;
  activeVersionNumber?: number | null;
  totalRuns: number;
  /** Runs in the last 24 hours — the list's "Runs 24 h" column. */
  runs24h: number;
  httpEnabled: boolean;
  workflowEnabled: boolean;
  lastRunAt?: string | null;
  lastDeployedAt?: string | null;
  lastUpdatedDate: string;
}

export interface IFunctionDetail {
  id: string;
  name: string;
  description?: string | null;
  status: FunctionStatus;
  isDirty: boolean;
  indexJs: string;
  packageJson: string;
  lockJson?: string | null;
  /** npm install/postinstall scripts run at build (native packages). Absent on older servers. */
  allowInstallScripts?: boolean;
  limits: IFunctionLimits;
  retry: IRetryPolicy;
  trigger: ITriggerConfig;
  outputActions: IOutputAction[];
  variables: IVariableBinding[];
  activeVersionId?: string | null;
  activeVersionNumber?: number | null;
  lastVersionNumber: number;
}

/**
 * The one profile every function runs under, as the server reports it.
 *
 * Not ceilings to choose under and not defaults to start from — these are the values. The console
 * displays them and never writes them, so a change to the platform profile reaches the screen
 * without anyone editing the client.
 */
export interface IFunctionLimitsOptions {
  cpuMillicores: number;
  memoryMb: number;
  timeoutSeconds: number;
  /** Concurrent runs of one function, across the whole fleet. Runs beyond it queue. */
  concurrency: number;
  /** Attempts including the first, so 2 is one retry. */
  attempts: number;
  retryDelaySeconds: number;
  /** Requests/minute and requests/day are modelled but hidden unless this is on. */
  showRateLimits: boolean;
}

export interface IFunctionAuditEvent {
  action: string;
  actorId?: string | null;
  actorEmail?: string | null;
  detail?: string | null;
  createdDate: string;
}

// ─── request/response payloads ──────────────────────────────────────────────

export type FunctionTemplate = "Minimal" | "HttpEcho" | "FetchTransform" | "ReusedConnection";

export interface ICreateFunctionPayload {
  name: string;
  description?: string | null;
  /** Which starter source to seed; the server falls back to the minimal handler. */
  template?: FunctionTemplate;
}

export interface IUpdateFunctionPayload {
  functionId: string;
  name: string;
  description?: string | null;
}

export interface ISaveFunctionPayload {
  functionId: string;
  indexJs: string;
  packageJson: string;
  lockJson?: string | null;
  allowInstallScripts: boolean;
  limits: IFunctionLimits;
  retry: IRetryPolicy;
  trigger: ITriggerConfig;
  outputActions: IOutputAction[];
  variables: IVariableBinding[];
}

export type FunctionSort = "Updated" | "Name";

export interface IGetFunctionsPayload {
  searchKey?: string;
  status?: string;
  /** Only fields of the function itself are sortable server-side. */
  sortBy?: FunctionSort;
  pageNumber: number;
  pageSize: number;
}

export interface IGetFunctionsResponse {
  data: IFunctionSummary[] | null;
  totalCount: number;
  errors?: unknown;
}

export interface IBaseResponse {
  isSuccess: boolean;
  errors?: Record<string, string> | null;
}

export interface IDeployFunctionPayload {
  functionId: string;
  note?: string | null;
  /** Build again instead of deploying this source's cached image. */
  rebuild?: boolean;
  /** Deploy exactly this build: the one a 202 answer named. Refused if the code changed since. */
  buildId?: string;
}
