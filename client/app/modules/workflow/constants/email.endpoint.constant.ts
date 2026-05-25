import { API_BASES } from "@/constants/endpoint.constant";

// Email Template endpoints
export const EMAIL_TEMPLATE_ENDPOINTS = {
  GET_TEMPLATES: `${API_BASES.LOGIC}/template/gets`,
} as const;

// Mail Configuration endpoints
export const MAIL_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `${API_BASES.LOGIC}/Mail/Gets`,
} as const;
