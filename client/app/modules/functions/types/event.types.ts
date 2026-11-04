/** The draft's event settings: the event names that start the function. */
export interface IEventSettings {
  functionId: string;
  triggerEvents: string[];
}

export interface ISaveEventSettingsPayload {
  functionId: string;
  triggerEvents: string[];
}

export interface IQueueStatus {
  httpQueued: number;
  eventQueued: number;
  oldestEventQueuedAt: string | null;
}
