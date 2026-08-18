import { API_BASES } from "@/constants/endpoint.constant";

const SCHEDULER_SUBPATH = "/Scheduler";

export const SCHEDULER_ENDPOINTS = {
  GET_ALL: `${API_BASES.WORKFLOW}${SCHEDULER_SUBPATH}/GetSchedules`,
  CREATE: `${API_BASES.WORKFLOW}${SCHEDULER_SUBPATH}/CreateSchedule`,
  UPDATE: `${API_BASES.WORKFLOW}${SCHEDULER_SUBPATH}/UpdateSchedule`,
  DELETE: `${API_BASES.WORKFLOW}${SCHEDULER_SUBPATH}/DeleteSchedule`,
} as const;
