/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly BLOCKS_API_BASE_URL: string;
  readonly BLOCKS_X_BLOCKS_KEY: string;
  readonly BLOCKS_GOOGLE_SITE_KEY: string;
  readonly BLOCKS_CONSTRUCT_URL: string;
  readonly BLOCKS_CLOUD_DASHBOARD_URL: string;
  readonly BLOCKS_UDS_API_BASE_URL: string;
  readonly BLOCKS_DATA_BASE_URL: string;
  readonly BLOCKS_IDP_API_BASE_URL: string;
  readonly BLOCKS_IAM_BASE_URL: string;
  readonly BLOCKS_IAM_CLIENT_ID: string
  readonly BLOCKS_AGENT_API_BASE_URL: string;
  readonly BLOCKS_EUROLM_API_BASE_URL: string;
  readonly BLOCKS_UTILITY_API_BASE_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
