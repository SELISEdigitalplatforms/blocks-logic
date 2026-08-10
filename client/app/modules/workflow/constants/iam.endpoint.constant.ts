const AUTH_SUBPATH = "/auth";

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `/api${AUTH_SUBPATH}/client-credentials`,
} as const;

export const IAM_AUTHORIZATION_ENDPOINTS = {
  GET_ORGANIZATIONS: "/api/iam/organizations",
  GET_ROLES: "/api/iam/roles",
  GET_PERMISSIONS: "/api/iam/permissions",
} as const;
