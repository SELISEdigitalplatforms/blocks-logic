export type RunStatus =
  | "Queued"
  | "Claimed"
  | "Starting"
  | "Running"
  | "OutputProcessing"
  | "Succeeded"
  | "Failed"
  | "TimedOut"
  | "Cancelled"
  | "ResourceExceeded"
  | "OutputFailed";

export type RunErrorCode =
  | "MemoryLimit"
  | "PidLimit"
  | "UserRuntimeError"
  | "RuntimeStartFailed"
  | "ResultTooLarge"
  | "ResultNotSerializable"
  | "ImagePullFailed"
  | "TimedOut"
  | "SandboxStartFailed"
  | "OutputActionFailed";

export type InvokedByType = "Http" | "Workflow" | "Test" | "Replay" | "Schedule" | "Event";

export const TERMINAL_RUN_STATUSES: RunStatus[] = [
  "Succeeded",
  "Failed",
  "TimedOut",
  "Cancelled",
  "ResourceExceeded",
  "OutputFailed",
];

export interface IRunSummary {
  id: string;
  functionId: string;
  versionNumber: number;
  status: RunStatus;
  errorCode?: RunErrorCode | null;
  invokedBy: InvokedByType;
  attempt: number;
  createdDate: string;
  completedAt?: string | null;
  durationMs?: number | null;
}

export interface IRunAttempt {
  number: number;
  status: RunStatus;
  errorCode: RunErrorCode | "None";
  errorMessage?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  durationMs?: number | null;
}

export interface IOutputActionResult {
  actionId: string;
  kind: string;
  ok: boolean;
  statusCode?: number | null;
  error?: string | null;
  durationMs: number;
  attempts: number;
}

export interface IRunDetail {
  id: string;
  functionId: string;
  versionNumber: number;
  status: RunStatus;
  errorCode?: RunErrorCode | null;
  errorMessage?: string | null;
  invokedBy: InvokedByType;
  invokedById?: string | null;
  input?: string | null;
  result?: string | null;
  attempt: number;
  maxAttempts: number;
  createdDate: string;
  startedAt?: string | null;
  completedAt?: string | null;
  durationMs?: number | null;
  peakMemoryBytes?: number | null;
  exitCode?: number | null;
  logsTruncated: boolean;
  attempts: IRunAttempt[];
  outputResults: IOutputActionResult[];
}

export interface IRunLogLine {
  seq: number;
  timestamp: string;
  level: string;
  message: string;
  data?: string | null;
}

export interface IGetRunsPayload {
  functionId?: string;
  status?: string;
  fromUtc?: string;
  toUtc?: string;
  pageNumber: number;
  pageSize: number;
}

export interface IGetRunsResponse {
  data: IRunSummary[] | null;
  totalCount: number;
}

export interface IGetRunLogsResponse {
  data: IRunLogLine[] | null;
  totalCount: number;
}

/** What Test/Invoke/Replay return: 202-shaped when still running, full result once terminal. */
export interface IInvokeResult {
  runId: string;
  status: RunStatus;
  result?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
}

export interface ITestFunctionPayload {
  functionId: string;
  inputJson?: string | null;
  waitTimeoutSeconds?: number | null;
}
