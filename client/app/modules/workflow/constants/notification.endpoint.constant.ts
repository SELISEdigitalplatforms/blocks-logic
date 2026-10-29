import { API_BASES } from "@/constants/endpoint.constant";

// Notification configuration endpoints
export const NOTIFICATION_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `${API_BASES.OS}/Notification/Gets`,
} as const;
