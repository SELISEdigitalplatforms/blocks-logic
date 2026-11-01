export enum ScheduleTriggerType {
  Webhook = 1,
  Queue = 2,
}

export enum ScheduleKind {
  Application = 1,
  Internal = 2,
}

export interface IWebhookConfiguration {
  url: string;
  method: string;
  headers?: Record<string, string> | null;
  signingSecret?: string | null;
}

export interface IQueueConfiguration {
  queueName: string;
}

export interface ISchedule {
  itemId: string;
  organizationId: string;
  name?: string | null;
  description?: string | null;
  payload: string;
  cronExpression: string;
  startDate?: string | null;
  endDate?: string | null;
  isActive: boolean;
  kind?: ScheduleKind;
  triggerType?: ScheduleTriggerType;
  webhook?: IWebhookConfiguration | null;
  queue?: IQueueConfiguration | null;
  /** IANA zone the cron runs in (SPEC6); absent or null = UTC. */
  timeZone?: string | null;
  /** Next fire (SPEC6); absent until that ships, null when no run is upcoming. */
  nextRunAtUtc?: string | null;
}

export interface IGetSchedulesPayload {
  searchKey: string;
  pageNumber: number;
  pageSize: number;
}

export interface IGetSchedulesResponse {
  data: ISchedule[] | null;
  totalCount: number;
  errors: unknown;
}

export interface ICreateSchedulePayload {
  name: string;
  description?: string | null;
  payload: string;
  cronExpression: string;
  startDate?: string | null;
  endDate?: string | null;
  webhook?: IWebhookConfiguration | null;
  /** IANA id; null = UTC. Update is a full replace, so omitting it stores UTC. */
  timeZone?: string | null;
}

export interface IPreviewScheduleRunsPayload {
  cronExpression: string;
  timeZone?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  count: number;
}

export interface IPreviewScheduleRunsResponse extends IBaseResponse {
  data: { occurrencesUtc: string[] } | null;
}

export interface IUpdateSchedulePayload extends ICreateSchedulePayload {
  itemId: string;
  isActive: boolean;
}

export interface IDeleteSchedulePayload {
  itemId: string;
}

export interface IBaseResponse {
  isSuccess: boolean;
  errors?: Record<string, string> | null;
}

export interface IBaseMutationResponse extends IBaseResponse {
  itemId?: string | null;
}

export type ScheduleExecutionOutcome = "Succeeded" | "Failed";

export interface IScheduleExecutionListItem {
  id: string;
  firedUtc?: string | null;
  finishedUtc?: string | null;
  durationMs?: number | null;
  status?: number | null;
  outcome: ScheduleExecutionOutcome;
}

export interface IScheduleExecution extends IScheduleExecutionListItem {
  scheduleId: string;
  scheduleName?: string | null;
  body?: string | null;
  snapshot?: string | null;
}

export interface IGetScheduleExecutionsPayload {
  scheduleId: string;
  outcome?: ScheduleExecutionOutcome | null;
  /** UTC ISO; startDate inclusive, endDate exclusive. */
  startDate?: string | null;
  endDate?: string | null;
  pageNumber: number;
  pageSize: number;
}

export interface IGetScheduleExecutionPayload {
  scheduleId: string;
  executionId: string;
}

export interface IGetScheduleExecutionsResponse extends IBaseResponse {
  data: IScheduleExecutionListItem[] | null;
  totalCount: number;
}

export interface IGetScheduleExecutionResponse extends IBaseResponse {
  data: IScheduleExecution | null;
}
