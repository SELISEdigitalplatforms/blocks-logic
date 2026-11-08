/** The draft's event settings: the action that starts the function, and whether actions are on. */
export interface IEventSettings {
  functionId: string;
  triggerEvents: string[];
  /** A missing value (an older server) means on. */
  actionsEnabled?: boolean;
}

export interface ISaveEventSettingsPayload {
  functionId: string;
  triggerEvents: string[];
  actionsEnabled: boolean;
}

export interface IQueueStatus {
  httpQueued: number;
  eventQueued: number;
  oldestEventQueuedAt: string | null;
}
