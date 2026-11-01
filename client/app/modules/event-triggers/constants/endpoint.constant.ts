import { API_BASES } from "@/constants/endpoint.constant";

const BLOCKS_EVENT_TRIGGER_SUBPATH = "/BlocksEventTrigger";

export const EVENT_TRIGGERS_ENDPOINTS = {
  GET_ALL: `${API_BASES.WORKFLOW}${BLOCKS_EVENT_TRIGGER_SUBPATH}/GetAll`,
  GET_WORKFLOW_EVENTS: `${API_BASES.WORKFLOW}${BLOCKS_EVENT_TRIGGER_SUBPATH}/GetWorkflowEvents`,
  GET_USAGE: `${API_BASES.WORKFLOW}${BLOCKS_EVENT_TRIGGER_SUBPATH}/GetUsage`,
  CAN_MANAGE: `${API_BASES.WORKFLOW}${BLOCKS_EVENT_TRIGGER_SUBPATH}/CanManage`,
  UPDATE: `${API_BASES.WORKFLOW}${BLOCKS_EVENT_TRIGGER_SUBPATH}/Update`,
} as const;

/** The page's path inside the current project scope. It has no menu item: the Blocks Event node links to it. */
export const EVENT_TRIGGERS_PATH = "event-triggers";
