// export const IDP_BASE_URL = "https://dev-idp.blocksdevelopers.com";

import { getRuntimeEnv } from "@/lib/runtime-env";

export const DEPLOYMENT_BASE_URL =
  "https://dev-deployment.blocksdevelopers.com";


export const API_BASES = {
  COMMUNICATION: "/api",
  CLOUD_CONFIGURATION: "/api",
  UDS: getRuntimeEnv("BLOCKS_UDS_API_BASE_URL") + "/api",
  UILM: "/api",
  UTILITIES: "/api",
  CLOUD_BUILD: "/api",
  IDP: getRuntimeEnv("BLOCKS_IDP_BASE_URL") + "/api",
  IDENTIFIER: "/api",
  LMT: "/api",
  MFA: "/api",
  ALERT: "/api",
  AI: getRuntimeEnv("BLOCKS_AGENT_API_BASE_URL") + "/api",
  STUDIO: "/api",
  WORKFLOW: "/api",
  EUROLM: getRuntimeEnv("BLOCKS_EUROLM_API_BASE_URL") + "/api",
  UTILITY: getRuntimeEnv("BLOCKS_UTILITY_API_BASE_URL") + "/api",
  LOGIC: getRuntimeEnv("BLOCKS_API_BASE_URL") + "/api",
} as const;
