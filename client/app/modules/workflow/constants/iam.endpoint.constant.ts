const AUTH_SUBPATH = "/auth";

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `/api${AUTH_SUBPATH}/client-credentials`,
} as const;
