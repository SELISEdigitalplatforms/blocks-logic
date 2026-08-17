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
