import { BuildStatus } from "./version.types";
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
  | "OutputActionFailed"
  | "Undeliverable"
  | "EnqueueFailed"
  | "Abandoned"
  | "SecretUnresolved"
  | "SecretStoreUnavailable"
  | "BuildFailed";

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
  peakMemoryBytes?: number | null;
  /** Reuse only: true = served by an already-running (warm) sandbox, false = cold start. */
  reused?: boolean | null;
  /** Reuse only: why the sandbox was replaced after this call (`dirty:<leftovers>`, `timeout`, …). */
  discardReason?: string | null;
  /** Reuse only: claim → input handed to the sandbox, in milliseconds. */
  handoverMs?: number | null;
}

/** One step of a call's time: `api`, `queue`, `handover` or `result`, then the step's name. */
export interface IRunTiming {
  group: string;
  step: string;
  ms: number;
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
  /** Total CPU time the sandbox consumed for the run, in milliseconds — cumulative, not a percentage. */
  cpuUsageMs?: number | null;
  /** Wall ms `cpuUsageMs` covers when it is the handler's own window; null = a total incl. start-up, never divide it. */
  cpuWindowMs?: number | null;
  exitCode?: number | null;
  logsTruncated: boolean;
  /** Reuse only: true = served by an already-running (warm) sandbox, false = cold start. */
  reused?: boolean | null;
  /** Reuse only: why the sandbox was replaced after this call (`dirty:<leftovers>`, `timeout`, …). */
  discardReason?: string | null;
  /** Reuse only: claim → input handed to the sandbox, in milliseconds. */
  handoverMs?: number | null;
  /** Where the call's time went, step by step; null for a run from before timings existed. */
  timings?: IRunTiming[] | null;
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
  /** Trigger filter — one of `InvokedByType`. */
  invokedBy?: string;
  /** Run-id search; matched as a prefix. */
  searchKey?: string;
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
  /** A run's status — or the build's, when {@link IInvokeResult.buildId} is set and no run exists yet. */
  status: RunStatus | BuildStatus;
  result?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
  /**
   * Returned instead of a run when the image was still building — there is nothing to invoke yet.
   * Poll the build with it and run again once it succeeds.
   */
  buildId?: string | null;
  buildStatus?: string | null;
}

export interface ITestFunctionPayload {
  functionId: string;
  inputJson?: string | null;
  waitTimeoutSeconds?: number | null;
  /** Build again instead of reusing this source's cached image — for one that is missing or wrong. */
  rebuild?: boolean;
}
