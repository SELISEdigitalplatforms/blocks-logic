const PLACEHOLDER_PREFIX = "__BLOCKS_";

type RuntimeKey =
  | "BLOCKS_API_BASE_URL"
  | "BLOCKS_X_BLOCKS_KEY"
  | "BLOCKS_GOOGLE_SITE_KEY"
  | "BLOCKS_CONSTRUCT_URL"
  | "BLOCKS_GITHUB_SSO_CLIENT_ID"
  | "BLOCKS_AGENT_API_BASE_URL"
  | "BLOCKS_EUROLM_API_BASE_URL"
  | "BLOCKS_UTILITIES_API_BASE_URL"
  | "BLOCKS_IDP_BASE_URL"
  | "BLOCKS_IAM_BASE_URL"
  | "BLOCKS_IAM_CLIENT_ID"
  | "BLOCKS_IAM_CALLBACK_URL"
  | "BLOCKS_UDS_API_BASE_URL"
  | "BLOCKS_OIDC_CLIENT_ID";

declare global {
  interface Window {
    __BLOCKS_ENV__?: Partial<Record<RuntimeKey, string>>;
  }
}

const isPlaceholder = (value?: string) =>
  !!value && value.startsWith(PLACEHOLDER_PREFIX) && value.endsWith("__");

export const getRuntimeEnv = (key: RuntimeKey): string => {
  const windowValue =
    typeof window !== "undefined" ? window.__BLOCKS_ENV__?.[key] : undefined;
  if (windowValue && !isPlaceholder(windowValue)) {
    return windowValue;
  }

  return import.meta.env[key] || "";
};
