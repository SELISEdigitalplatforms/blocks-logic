import { API_BASES } from "@/constants/endpoint.constant";
import { getRuntimeEnv } from "@/lib/runtime-env";

// ─── Subpaths ─────────────────────────────────────────────────────────────────

const BLOCKS_IDP_BASE_URL = getRuntimeEnv("BLOCKS_IDP_BASE_URL");
const AUTH_SUBPATH = "/auth";
const AUTH_OIDC_SUBPATH = "/oidc";

// ─── Auth endpoints (auth.service) ───────────────────────────────────────────

export const AUTH_ENDPOINTS = {
  TOKEN: `${BLOCKS_IDP_BASE_URL}${AUTH_SUBPATH}/Token`,
  GET_SOCIAL_LOGIN_ENDPOINT: `${BLOCKS_IDP_BASE_URL}${AUTH_SUBPATH}/GetSocialLogInEndPoint`,
  GET_LOGIN_OPTIONS: `${BLOCKS_IDP_BASE_URL}${AUTH_SUBPATH}/GetLoginOptions`,
  USER_INFO: `${BLOCKS_IDP_BASE_URL}/idp/UserInfo`,
  LOGOUT: `${BLOCKS_IDP_BASE_URL}/api${AUTH_SUBPATH}/Logout`,
} as const;

// ─── Client credential endpoints (auth-clients.service) ─────────────────────

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `/api/Authentication/GetClientCredentials`,
  SAVE_CLIENT_CREDENTIAL: `/api${AUTH_SUBPATH}/SaveClientCredential`,
  DELETE_CLIENT_CREDENTIAL: `/api${AUTH_SUBPATH}/DeleteClientCredential`,
} as const;

// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

export const AUTH_OIDC_ENDPOINTS = {
  GET_OIDC_CLIENTS: `${BLOCKS_IDP_BASE_URL}/api/oidc-clients`,
  GET_OIDC_CLIENT: `${BLOCKS_IDP_BASE_URL}/api/oidc-clients`,
  SAVE_OIDC_CLIENT: `${BLOCKS_IDP_BASE_URL}/api/oidc-clients`,
  DELETE_OIDC_CLIENT: `${BLOCKS_IDP_BASE_URL}/api/oidc-clients`,
  OIDC_TOKEN: `${BLOCKS_IDP_BASE_URL}/api${AUTH_OIDC_SUBPATH}/token`,
} as const;

// ─── Auth configuration endpoints (auth-config.service) ─────────────────────

export const AUTH_CONFIG_ENDPOINTS = {
  GET_CONFIG: `${BLOCKS_IDP_BASE_URL}/api${AUTH_SUBPATH}/Get`,
  UPDATE_CONFIG: `${BLOCKS_IDP_BASE_URL}/api${AUTH_SUBPATH}/Update`,
} as const;

// ─── SSO endpoints (social.service) ─────────────────────────────────────────

export const SSO_ENDPOINTS = {
  GET_SSO_CREDENTIALS: `/api${AUTH_SUBPATH}/GetSsoCredentials`,
  GET_SSO_CREDENTIAL: `/api${AUTH_SUBPATH}/GetSsoCredential`,
  SAVE_SSO_CREDENTIAL: `/api${AUTH_SUBPATH}/SaveSsoCredential`,
  DELETE_SSO_CREDENTIAL: `/api${AUTH_SUBPATH}/DeleteSsoCredential`,
  UPDATE_STATUS: `/api${AUTH_SUBPATH}/UpdateStatus`,
} as const;

// ─── OIDC flow endpoints (oidc-auth-flow.service) ───────────────────────────

export const OIDC_FLOW_ENDPOINTS = {
  USER_ACKNOWLEDGEMENT: `/api${AUTH_SUBPATH}/UserAcknowledgement`,
} as const;

export const IMPERSONATE_ENDPOINTS = {
  IMPERSONATE: `${BLOCKS_IDP_BASE_URL}/api${AUTH_SUBPATH}/impersonate`,
  STOP_IMPERSONATION: `${BLOCKS_IDP_BASE_URL}/api${AUTH_SUBPATH}/impersonation/stop`,
    IMPERSONATION_STATUS: `${API_BASES.IDP}/auth/impersonation/status`,

} as const;

// ─── People endpoints ─────────────────────────────────────────────────────────

export const PEOPLE_ENDPOINTS = {
  SIGNUP: `${API_BASES.IDENTIFIER}/People/Signup`,
} as const;
// ─── Legacy re-export (backward compat for oauth.service) ───────────────────

export const IDP_ENDPOINTS = {
  AUTHENTICATION: {
    GET_SOCIAL_LOGIN_ENDPOINT: AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT,
    TOKEN: AUTH_ENDPOINTS.TOKEN,
  }
};
// ─── Identity Provider endpoints (identity-provider.service) ─────────────────

export const IDENTITY_PROVIDER_ENDPOINTS = {
  GET_ALL: `${BLOCKS_IDP_BASE_URL}/api/identity-providers`,
  GET_BY_ID: `${BLOCKS_IDP_BASE_URL}/api/identity-providers`,
  CREATE: `${BLOCKS_IDP_BASE_URL}/api/identity-providers`,
  UPDATE: `${BLOCKS_IDP_BASE_URL}/api/identity-providers`,
  UPDATE_STATUS: `${BLOCKS_IDP_BASE_URL}/api/identity-providers`,
} as const;

export const EXECUTION_CONTEXT_ENDPOINTS = {
  CONTEXT: `${BLOCKS_IDP_BASE_URL}/api${AUTH_SUBPATH}/context`,
};
