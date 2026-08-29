import { getRuntimeEnv } from "@seliseblocks/genesis-os";
import { HttpClient } from "@seliseblocks/genesis-os";
import {
  createHttpFailureReporter,
  getRollbar,
} from "@seliseblocks/genesis-os/observability";
import { SERVICE_NAME } from "@/constants/service.constant";

const reportHttpFailure = createHttpFailureReporter(
  getRollbar({ service: SERVICE_NAME }),
);

export const serviceInstances = {
  agentsService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_AGENTS_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
    onError: reportHttpFailure,
  }),
  dataService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_DATA_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
    onError: reportHttpFailure,
  }),
  iamService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_IAM_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
    onError: reportHttpFailure,
  }),
  logicService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
    onError: reportHttpFailure,
  }),
};

export { HttpClient };
