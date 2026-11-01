import { RunErrorCode } from "../types/run.types";
import { tStatic } from "@seliseblocks/genesis-os";

/**
 * A run's error code as a sentence the developer can act on — §4.8 of the UI spec. The raw code is
 * still shown next to it, so nothing is hidden by the friendlier wording.
 */
export const RUN_ERROR_EXPLANATIONS: Record<RunErrorCode, string> = {
  get MemoryLimit() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_FUNCTION_USED_MORE"); },
  get PidLimit() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_FUNCTION_STARTED_MORE"); },
  get UserRuntimeError() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_HANDLER_THREW_THE"); },
  get RuntimeStartFailed() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_SANDBOX_COULD_NOT"); },
  get ResultTooLarge() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_RETURNED_VALUE_IS"); },
  get ResultNotSerializable() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_RETURNED_VALUE_COULD"); },
  get ImagePullFailed() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_IMAGE_COULD_NOT"); },
  get TimedOut() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_RUN_PASSED_ITS"); },
  get SandboxStartFailed() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_SANDBOX_FAILED_TO"); },
  get OutputActionFailed() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_FUNCTION_RETURNED_SUCCESSFULLY"); },
  get Undeliverable() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.NO_RUNNER_EVER_PICKED"); },
  get EnqueueFailed() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_RUN_COULD_NOT"); },
  get Abandoned() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_RUN_NEVER_REPORTED"); },
  get SecretUnresolved() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.A_VARIABLE_IS_BOUND"); },
  get BuildFailed() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_TEST_S_IMAGE"); },
  get SecretStoreUnavailable() { return tStatic("logic.functions", "RUN_ERROR_CONSTANT.THE_SECRET_STORE_COULD"); },
};

/**
 * The bootstrap's prefix for a run that died while *importing* index.js, before the handler was
 * ever called (`runtime/bootstrap.mjs`). It is worth telling apart from any other
 * `UserRuntimeError`: the generic sentence for that code sends the reader to a stack in the logs,
 * and a module that never loaded has written no logs at all.
 */
const MODULE_LOAD_PREFIX = "the function module failed to load";

/** `ctx is not defined` / `input is not defined` — the handler's parameters used at module scope. */
const HANDLER_SCOPE = /\b(ctx|input)\b is not defined/i;

/** Node's shape for an import of something that is not installed. */
const MISSING_PACKAGE = /cannot find (package|module)|ERR_MODULE_NOT_FOUND/i;

/**
 * The hint for a failure, or null when there is no hint to add.
 *
 * It deliberately does not fall back to the raw message: callers render that themselves, and
 * returning it here made the two indistinguishable — a known code showed the friendly sentence
 * *instead of* what actually happened, so "Cannot find package 'ky' imported from /function/index.js"
 * could only be read in the network tab.
 *
 * `errorMessage` refines the code rather than replacing it: one code covers everything a tenant's
 * module can do, and the most common first failure — reaching for `ctx` outside the handler — is
 * indistinguishable from a thrown handler unless the message is read.
 */
export const explainRunError = (
  errorCode?: RunErrorCode | string | null,
  errorMessage?: string | null,
) => {
  const message = errorMessage ?? "";

  if (message.toLowerCase().startsWith(MODULE_LOAD_PREFIX)) {
    if (HANDLER_SCOPE.test(message)) {
      return tStatic("logic.functions", "RUN_ERROR_CONSTANT.HANDLER_SCOPE");
    }
    if (MISSING_PACKAGE.test(message)) {
      return tStatic("logic.functions", "RUN_ERROR_CONSTANT.MISSING_PACKAGE");
    }
    return tStatic("logic.functions", "RUN_ERROR_CONSTANT.MODULE_THREW");
  }

  if (!errorCode) return null;
  return RUN_ERROR_EXPLANATIONS[errorCode as RunErrorCode] ?? null;
};
