import { API_BASES } from "@/constants/endpoint.constant";

export const AI_ENDPOINTS = {
  AGENT_QUERIES: `${API_BASES.AGENTS}/agents/queries`,
} as const;
