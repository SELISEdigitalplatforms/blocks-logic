import { getRuntimeEnv } from "@seliseblocks/genesis-os";

/** The Logic API is served by the same host as its frontend in the browser. */
export const getLogicBaseUrl = (): string =>
  typeof window === "undefined"
    ? getRuntimeEnv("BLOCKS_LOGIC_BASE_URL")
    : window.location.origin;
