export type FunctionEventType = "SendMail" | "FunctionEvent";

/** One event type a function may send, with the payload it starts from. */
export interface IEventCatalogItem {
  type: FunctionEventType;
  title: string;
  description: string;
  /** False = a public (anonymous) function may not send it. */
  publicAllowed: boolean;
  payloadTemplate: Record<string, unknown>;
}

export interface IEventCatalog {
  items: IEventCatalogItem[];
}

export interface IFunctionEvent {
  type: FunctionEventType;
  payload: Record<string, unknown>;
}

/** The draft's event settings: what the function may send and which events start it. */
export interface IEventSettings {
  functionId: string;
  events: IFunctionEvent[];
  triggerEvents: string[];
}

export interface ISaveEventSettingsPayload {
  functionId: string;
  events: IFunctionEvent[];
  triggerEvents: string[];
}

export type RunEventStatus = "Sent" | "Failed" | "Rejected";

export interface IRunEvent {
  id: string;
  type: string;
  name: string | null;
  status: RunEventStatus;
  error: string | null;
  attempts: number;
  sentAt: string | null;
}

export interface IRunEventsResponse {
  items: IRunEvent[];
  /** Why the run's events were dropped, when they were. */
  rejected: string | null;
}

export interface IQueueStatus {
  httpQueued: number;
  eventQueued: number;
  oldestEventQueuedAt: string | null;
}
