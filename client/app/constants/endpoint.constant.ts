enum Version {
  V1 = "v1",
}

// Check if using localhost to determine API path structure
const isLocalhost = import.meta.env.BLOCKS_API_BASE_URL?.includes("localhost");

export const API_BASES = {
  COMMUNICATION: isLocalhost ? "/Api" : `/communication/${Version.V1}`,
  CLOUD_CONFIGURATION: isLocalhost ? "/Api" : `/cloudconfiguration/${Version.V1}`,
  UDS: isLocalhost ? "/Api" : `/uds/${Version.V1}`,
  UILM: isLocalhost ? "/Api" : `/uilm/${Version.V1}`,
  UTILITIES: isLocalhost ? "/Api" : `/utilities/${Version.V1}`,
  CLOUD_BUILD: isLocalhost ? "/Api" : `/cloudbuild/${Version.V1}`,
  IDP: isLocalhost ? "/Api" : `/idp/${Version.V1}`,
  IDENTIFIER: isLocalhost ? "/Api" : `/identifier/${Version.V1}`,
  LMT: isLocalhost ? "/Api" : `/lmt/${Version.V1}`,
  MFA: isLocalhost ? "/Api" : `/mfa/${Version.V1}`,
  ALERT: isLocalhost ? "/Api" : `/alert/${Version.V1}`,
  AI: isLocalhost ? "/Api" : `/blocksai-api/${Version.V1}`,
  STUDIO: isLocalhost ? "/Api" : `/studio/${Version.V1}`,
} as const;
