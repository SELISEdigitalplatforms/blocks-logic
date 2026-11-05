import { API_BASES } from "@/constants/endpoint.constant";

const ACTIONS_SUBPATH = "/Actions";

/** The tenant's actions the editors may pick. blocks-os owns the list and its switches; logic only reads it. */
export const ACTIONS_ENDPOINTS = {
  GET_FOR_WORKFLOW: `${API_BASES.WORKFLOW}${ACTIONS_SUBPATH}/GetForWorkflow`,
  GET_FOR_FUNCTION: `${API_BASES.WORKFLOW}${ACTIONS_SUBPATH}/GetForFunction`,
} as const;
