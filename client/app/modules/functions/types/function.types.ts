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

export interface ITriggerConfig {
  httpEnabled: boolean;
  authMode: AuthMode;
  roles: string[];
  permissions: string[];
  roleMatch: MatchMode;
  permissionMatch: MatchMode;
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
  limits: IFunctionLimits;
  retry: IRetryPolicy;
  trigger: ITriggerConfig;
  outputActions: IOutputAction[];
  variables: IVariableBinding[];
  activeVersionId?: string | null;
  activeVersionNumber?: number | null;
  lastVersionNumber: number;
}

export interface IFunctionLimitsOptions {
  ceilingCpuMillicores: number;
  ceilingMemoryMb: number;
  ceilingTimeoutSeconds: number;
  minConcurrency: number;
  maxConcurrency: number;
  defaultCpuMillicores: number;
  defaultMemoryMb: number;
  defaultTimeoutSeconds: number;
  defaultConcurrency: number;
  /** Requests/minute and requests/day are modelled but hidden unless this is on. */
  showRateLimits: boolean;
}

export interface ISecretCatalogEntry {
  id: string;
  name: string;
}

export interface IFunctionAuditEvent {
  action: string;
  actorId?: string | null;
  actorEmail?: string | null;
  detail?: string | null;
  createdDate: string;
}

// ─── request/response payloads ──────────────────────────────────────────────

export type FunctionTemplate = "Minimal" | "HttpEcho" | "FetchTransform";

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
}

export interface IRollbackFunctionPayload {
  functionId: string;
  versionNumber: number;
}
