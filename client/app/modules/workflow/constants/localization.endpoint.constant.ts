import { API_BASES } from "@/constants/endpoint.constant";

const LANGUAGE_SUBPATH = "/Language";

// Language endpoints
export const LANGUAGE_ENDPOINTS = {
  GETS: `${API_BASES.LOGIC}${LANGUAGE_SUBPATH}/Gets`,
} as const;
