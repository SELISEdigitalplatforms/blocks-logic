import { RunErrorCode } from "../types/run.types";

/**
 * A run's error code as a sentence the developer can act on — §4.8 of the UI spec. The raw code is
 * still shown next to it, so nothing is hidden by the friendlier wording.
 */
export const RUN_ERROR_EXPLANATIONS: Record<RunErrorCode, string> = {
  MemoryLimit:
    "The function used more memory than its limit. Raise the memory limit (max 200 MB) or hold less in memory.",
  PidLimit: "The function started more than 64 processes or threads.",
  UserRuntimeError: "The handler threw. The stack is in the logs below.",
  RuntimeStartFailed:
    "The sandbox could not start the runtime — usually a broken package.json or a package that needs a native build.",
  ResultTooLarge: "The returned value is over 5 MB. Return a reference instead of the payload.",
  ResultNotSerializable:
    "The returned value could not be turned into JSON. Return plain objects, arrays and primitives.",
  ImagePullFailed:
    "The image could not be pulled — it is no longer on the registry. The build behind it has been invalidated, so testing or deploying again builds a new one.",
  TimedOut:
    "The run passed its timeout and was stopped. Raise the timeout (max 90 s) or do less work per call.",
  SandboxStartFailed: "The sandbox failed to start. Nothing ran, so a replay is safe.",
  OutputActionFailed: "The function returned successfully, but an output action did not deliver.",
  Undeliverable:
    "No runner ever picked this up, so nothing ran. It is safe to run again — but check the runners are healthy first, because the platform already gave up on delivering it once.",
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
      return (
        "input and ctx are parameters of your handler — they exist only inside " +
        "export default async function handler(input, ctx) { … }, not at the top level of the file. " +
        "Code outside the handler runs once when the sandbox starts, which is where clients, caches " +
        "and constants belong; anything that needs the request or the context has to be inside."
      );
    }
    if (MISSING_PACKAGE.test(message)) {
      return (
        "The import could not be resolved. Add the package to package.json — pinned to an exact " +
        "version — then run the test again, which builds a new image."
      );
    }
    return (
      "The file threw while it was being imported, so the handler never ran and nothing was logged. " +
      "The cause is in top-level code, not inside your handler."
    );
  }

  if (!errorCode) return null;
  return RUN_ERROR_EXPLANATIONS[errorCode as RunErrorCode] ?? null;
};
