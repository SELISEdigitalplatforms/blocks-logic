export const CLOUD_BUILD_ENDPOINTS = {
  // Authentication & Authorization
  ACCESS_TOKEN: "/api/cloudbuild/github/access-token",
  IS_AUTHORIZED: "/api/cloudbuild/github/is-authorized",
  REMOVE_AUTHORIZATION: "/api/cloudbuild/github/remove-authorization",
  REMOVE_ACCESS_TOKEN: "/api/cloudbuild/github/remove-access-token",

  // GitHub Repositories
  GITHUB_REPOS: "/api/cloudbuild/github/repositories",
  GITHUB_USER: "/api/cloudbuild/github/user",
  GITHUB_BRANCHES: "/api/cloudbuild/github/branches",
  GITHUB_BRANCH_EXISTS: "/api/cloudbuild/github/branch-exists",

  // Build & Deployment
  BUILD_BUILD: "/api/cloudbuild/build/clone",
  RUN_BUILD: "/api/cloudbuild/build/run",
  MANUAL: "/api/cloudbuild/build/manual",
  BUILD: "/api/cloudbuild/build",

  // Repository Management
  REPOS: "/api/cloudbuild/repos",
  REPOS_LIST: "/api/cloudbuild/repos/list",
  REPO_DETAILS: "/api/cloudbuild/repos/details",

  // Build Settings
  SETTINGS: "/api/cloudbuild/settings",
};

export const MIGRATION_ENDPOINTS = {
  GET_STATUS: "/api/identifier/migration/status",
  START_MIGRATION: "/api/identifier/migration/start",
};
