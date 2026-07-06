import { getRuntimeEnv } from "@/lib/runtime-env";
import { HttpClient } from "@seliseblocks/blocks-kit";

export const serviceInstances = {
  
  agentsService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_AGENTS_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
  dataService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_DATA_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
  iamService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_IAM_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
  logicService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
};

export { HttpClient };
