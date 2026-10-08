import { useEffect, useRef, useState } from "react";
import { useDeployFunction } from "./use-functions";
import { useGetBuild } from "./use-versions";
import { IFunctionVersionSummary, isDeployPending } from "../types/version.types";

interface UseDeployOptions {
  onDeployed: (version: IFunctionVersionSummary) => void;
  onFailed: (error: unknown) => void;
}

/**
 * Deploy without holding one request for the whole build (F-4). The Api waits ~20 s; a build
 * still running then answers 202 with its id. This watches that build (GetBuild, every 2 s) and,
 * once it succeeds, deploys exactly that build — the Api refuses it if the code was saved again
 * meanwhile, so what ships is what was built. A failed build is reported once and its id kept, so
 * the build log stays on screen until the next deploy.
 *
 * Leaving the page stops the watching, not the build: the next Deploy finds the finished build
 * and is instant.
 */
/**
 * A deploy refused because its build already failed carries that build's id in `errors.buildId`
 * (PKG-14). Split it off: it is not a message for the toast, it is the build whose log to show.
 */
export const splitFailedBuild = (error: unknown): { error: unknown; buildId?: string } => {
  const errors = (error as { errors?: unknown } | null)?.errors;
  if (!errors || typeof errors !== "object" || Array.isArray(errors)) return { error };
  const { buildId, ...rest } = errors as Record<string, unknown>;
  if (typeof buildId !== "string" || buildId.length === 0) return { error };
  return { error: { ...(error as object), errors: rest }, buildId };
};

export const useDeploy = (functionId: string, { onDeployed, onFailed }: UseDeployOptions) => {
  const { mutateAsync, isPending } = useDeployFunction();
  const [buildId, setBuildId] = useState<string>();
  // The failed build a refused deploy named: known to be over before GetBuild answers.
  const [refusedBuildId, setRefusedBuildId] = useState<string>();
  const { data: build } = useGetBuild(buildId);
  // The build whose outcome was already handled: the effect re-runs on every render.
  const handled = useRef<string | null>(null);
  // The page's latest callbacks, without making them effect dependencies.
  const callbacks = useRef({ onDeployed, onFailed });
  useEffect(() => {
    callbacks.current = { onDeployed, onFailed };
  });

  const settle = (result: Awaited<ReturnType<typeof mutateAsync>>) => {
    if (isDeployPending(result)) {
      handled.current = null;
      setBuildId(result.buildId);
      return;
    }
    setBuildId(undefined);
    callbacks.current.onDeployed(result);
  };

  // Report a refused deploy once. When it names a failed build, keep that build on screen so its
  // log shows, and mark it handled so the GetBuild effect does not report it a second time.
  const fail = (error: unknown) => {
    const { error: shown, buildId: failedBuildId } = splitFailedBuild(error);
    handled.current = failedBuildId ?? null;
    setRefusedBuildId(failedBuildId);
    setBuildId(failedBuildId);
    callbacks.current.onFailed(shown);
  };

  const deploy = async () => {
    handled.current = null;
    setRefusedBuildId(undefined);
    setBuildId(undefined);
    try {
      settle(await mutateAsync({ functionId }));
    } catch (error) {
      fail(error);
    }
  };

  useEffect(() => {
    if (!buildId || build?.id !== buildId || handled.current === buildId) return;
    if (build.status === "Failed") {
      handled.current = buildId;
      callbacks.current.onFailed({
        errors: `The build failed: ${build.errorMessage ?? "unknown error"}`,
      });
      return;
    }
    if (build.status !== "Succeeded") return;
    handled.current = buildId;
    mutateAsync({ functionId, buildId })
      .then(settle)
      .catch(fail);
    // settle and mutateAsync are stable for this purpose; the build's status drives the effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [build, buildId, functionId]);

  // A build named by a refused deploy is already known to have failed, before GetBuild answers.
  const isWaitingForBuild = !!buildId && refusedBuildId !== buildId && build?.status !== "Failed";

  return {
    deploy,
    /** The request is in flight, or a build it started is still running. */
    isBuilding: isPending || isWaitingForBuild,
    /** The build to show progress (and, when it failed, its log) for. */
    buildId,
  };
};
