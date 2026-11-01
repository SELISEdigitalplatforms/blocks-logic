/** One platform event of the tenant's Blocks Event Trigger catalog (`BlocksEventTrigger/GetAll`). */
export interface IBlocksEventCatalogItem {
  eventName: string;
  label: string;
  description: string;
  serviceKey: string;
  sourceServiceKeys: string[];
  consumerNames: string[];
  isValidForWorkflow: boolean;
  isValidForFunction: boolean;
}

export interface IBlocksEventCatalogResponse {
  /** False when the tenant has no catalog document: an operator still has to run the seed. */
  isSeeded: boolean;
  items: IBlocksEventCatalogItem[];
}

/** An event the Blocks Event node may listen to (`GetWorkflowEvents`: enabled for workflows only). */
export interface IBlocksEventWorkflowOption {
  eventName: string;
  label: string;
  serviceKey: string;
  description: string;
}

export interface IBlocksEventWorkflowEventsResponse {
  items: IBlocksEventWorkflowOption[];
}

export interface IBlocksEventUsageResponse {
  publishedWorkflowCount: number;
}

/** Which flag a switch controls. */
export type BlocksEventFlag = "isValidForWorkflow" | "isValidForFunction";

export interface IUpdateBlocksEventFlagPayload {
  eventName: string;
  isValidForWorkflow?: boolean;
  isValidForFunction?: boolean;
}

export interface IUpdateBlocksEventFlagResponse {
  isSuccess: boolean;
  itemId: string | null;
  errors: Record<string, string> | null;
  affectedPublishedWorkflowCount: number;
}
