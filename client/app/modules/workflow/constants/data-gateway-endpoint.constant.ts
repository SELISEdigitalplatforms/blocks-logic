// ─── Data Source endpoints ────────────────────────────────────────────────────

import { API_BASES } from "@/constants/endpoint.constant";

const DATA_SOURCES_SUBPATH = "/data-sources";

export const DATA_SOURCE_ENDPOINTS = {
  ADD: `${API_BASES.UDS}${DATA_SOURCES_SUBPATH}/add`,
  UPDATE: `${API_BASES.UDS}${DATA_SOURCES_SUBPATH}/update`,
  GET: `${API_BASES.UDS}${DATA_SOURCES_SUBPATH}`,
} as const;

// ─── Schema endpoints ─────────────────────────────────────────────────────────

const SCHEMAS_SUBPATH = "/schemas";

export const SCHEMA_ENDPOINTS = {
  LIST: `${API_BASES.UDS}${SCHEMAS_SUBPATH}`,
  DETAILS: `${API_BASES.UDS}${SCHEMAS_SUBPATH}`,
  CREATE_INFO: `${API_BASES.UDS}${SCHEMAS_SUBPATH}/info`,
  UPDATE_INFO: `${API_BASES.UDS}${SCHEMAS_SUBPATH}/info`,
  UPDATE_FIELDS: `${API_BASES.UDS}${SCHEMAS_SUBPATH}/fields`,
  DELETE: `${API_BASES.UDS}${SCHEMAS_SUBPATH}`,
  UNADAPTED_CHANGE_LOGS: `${API_BASES.UDS}${SCHEMAS_SUBPATH}/unadapted-change-logs`,
} as const;

// ─── Data Access endpoints ────────────────────────────────────────────────────

const DATA_ACCESS_SUBPATH = "/data-access";
const POLICY_SUBPATH = "/policy";

export const DATA_ACCESS_ENDPOINTS = {
  MANAGE: `${API_BASES.UDS}${DATA_ACCESS_SUBPATH}/manage`,
  SECURITY_CHANGE: `${API_BASES.UDS}${DATA_ACCESS_SUBPATH}/security/change`,
  POLICY_GET: `${API_BASES.UDS}${DATA_ACCESS_SUBPATH}${POLICY_SUBPATH}`,
  POLICY_CREATE: `${API_BASES.UDS}${DATA_ACCESS_SUBPATH}${POLICY_SUBPATH}/create`,
  POLICY_UPDATE: `${API_BASES.UDS}${DATA_ACCESS_SUBPATH}${POLICY_SUBPATH}/update`,
  POLICY_DELETE: `${API_BASES.UDS}${DATA_ACCESS_SUBPATH}${POLICY_SUBPATH}`,
} as const;

// ─── Data Manage endpoints ────────────────────────────────────────────────────

const DATA_MANAGE_SUBPATH = "/data-manage";

export const DATA_MANAGE_ENDPOINTS = {
  GET_MOCK_DATA: `${API_BASES.UDS}${DATA_MANAGE_SUBPATH}`,
  DELETE_MOCK_DATA: `${API_BASES.UDS}${DATA_MANAGE_SUBPATH}/mock-data`,
} as const;

// ─── Data Validation endpoints ────────────────────────────────────────────────

const DATA_VALIDATIONS_SUBPATH = "/data-validations";

export const DATA_VALIDATION_ENDPOINTS = {
  GET: `${API_BASES.UDS}${DATA_VALIDATIONS_SUBPATH}/by-schema-and-field`,
  CREATE: `${API_BASES.UDS}${DATA_VALIDATIONS_SUBPATH}`,
  UPDATE: `${API_BASES.UDS}${DATA_VALIDATIONS_SUBPATH}`,
  DELETE: `${API_BASES.UDS}${DATA_VALIDATIONS_SUBPATH}`,
} as const;

// ─── Gateway & Configuration endpoints ────────────────────────────────────────

export const GATEWAY_ENDPOINTS = {
  EXECUTE: `${API_BASES.UDS}`,
  RELOAD: `${API_BASES.UDS}`,
  PING: `${API_BASES.UDS}`,
} as const;

// ─── Pipeline endpoints ───────────────────────────────────────────────────────

export const PIPELINE_ENDPOINTS = {
  INITIATE: `${API_BASES.UDS}/deployment/pipeline`,
} as const;
