import { RunErrorCode } from "../types/run.types";

/**
 * A run's error code as a sentence the developer can act on — §4.8 of the UI spec. The raw code is
 * still shown next to it, so nothing is hidden by the friendlier wording.
 */
export const RUN_ERROR_EXPLANATIONS: Record<RunErrorCode, string> = {
  MemoryLimit:
    "The function used more memory than its limit. Raise the memory limit (max 300 MB) or hold less in memory.",
  PidLimit: "The function started more than 64 processes or threads.",
  UserRuntimeError: "The handler threw. The stack is in the logs below.",
  RuntimeStartFailed:
    "The sandbox could not start the runtime — usually a broken package.json or a package that needs a native build.",
  ResultTooLarge: "The returned value is over 5 MB. Return a reference instead of the payload.",
  ResultNotSerializable:
    "The returned value could not be turned into JSON. Return plain objects, arrays and primitives.",
  ImagePullFailed: "The deployed image could not be pulled. Deploy again to rebuild it.",
  TimedOut:
    "The run passed its timeout and was stopped. Raise the timeout (max 60 s) or do less work per call.",
  SandboxStartFailed: "The sandbox failed to start. Nothing ran, so a replay is safe.",
  OutputActionFailed: "The function returned successfully, but an output action did not deliver.",
};

export const explainRunError = (
  errorCode?: RunErrorCode | string | null,
  errorMessage?: string | null,
) => {
  if (!errorCode) return errorMessage ?? null;
  return RUN_ERROR_EXPLANATIONS[errorCode as RunErrorCode] ?? errorMessage ?? null;
};
