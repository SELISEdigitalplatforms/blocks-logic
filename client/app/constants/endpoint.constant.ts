import { getRuntimeEnv } from "@seliseblocks/blocks-kit";

const BLOCKS_IAM_BASE_URL = getRuntimeEnv("BLOCKS_IAM_BASE_URL");
const AUTH_OIDC_SUBPATH = "/oidc";


export const API_BASES = {
  COMMUNICATION: "/api",
  CLOUD_CONFIGURATION: "/api",
  DATA: getRuntimeEnv("BLOCKS_DATA_BASE_URL") + "/api",
  UTILITIES: "/api",
  CLOUD_BUILD: "/api",
  IAM: getRuntimeEnv("BLOCKS_IAM_BASE_URL") + "/api",
  IDENTIFIER: "/api",
  LMT: "/api",
  MFA: "/api",
  ALERT: "/api",
  AGENTS: getRuntimeEnv("BLOCKS_AGENTS_BASE_URL") + "/api",
  STUDIO: "/api",
  WORKFLOW: "/api",
  LOCALIZATION: getRuntimeEnv("BLOCKS_LOCALIZATION_BASE_URL") + "/api",
  LOGIC: getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") + "/api",
} as const;


// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

export const AUTH_OIDC_ENDPOINTS = {
  OIDC_TOKEN: `${BLOCKS_IAM_BASE_URL}/api${AUTH_OIDC_SUBPATH}/token`,
} as const;