
import { API_BASES } from "@/constants/endpoint.constant";

const SCHEMAS_SUBPATH = "/schemas";

export const SCHEMA_ENDPOINTS = {
  LIST: `${API_BASES.DATA}${SCHEMAS_SUBPATH}`,
  DETAILS: `${API_BASES.DATA}${SCHEMAS_SUBPATH}/get-by-id`,
} as const;
