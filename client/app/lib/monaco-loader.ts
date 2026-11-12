import { useEffect, useState } from "react";
import { loader, type Monaco, type OnMount } from "@monaco-editor/react";
import { getRollbar } from "@seliseblocks/genesis-os/observability";
import { SERVICE_NAME } from "@/constants/service.constant";

/**
 * Where Monaco's AMD build is served from: our own origin, not jsdelivr.
 *
 * vite.config.ts copies node_modules/monaco-editor/min/vs here on build and serves it in dev, so
 * the CSP needs no third-party script host. Still the AMD loader, not an ESM import: importing
 * `monaco-editor` directly makes Vite transform its whole ESM tree and exhausts the JS heap.
 * The version is the exact one pinned in package.json (same as the loader's old CDN default).
 */
export const MONACO_VS_PATH = "/monaco/vs";

loader.config({ paths: { vs: MONACO_VS_PATH } });

// `Monaco` from @monaco-editor/react points at a monaco-editor subpath whose types this project
// does not resolve, so it is untyped here; the editor instance is typed. Callbacks that take a
// model or a position (completion providers) are typed from it instead.
type EditorInstance = Parameters<OnMount>[0];
export type MonacoTextModel = NonNullable<ReturnType<EditorInstance["getModel"]>>;
export type MonacoPosition = NonNullable<ReturnType<EditorInstance["getPosition"]>>;

/** What `loader.init()`'s `cancel()` rejects with: an expected unmount, not a failure. */
const isCancelation = (reason: unknown) =>
  (reason as { type?: unknown } | null | undefined)?.type === "cancelation";

// Rollbar takes strings, errors and objects; a bare primitive or `null` goes in as text so the
// item still records what the loader rejected with.
const toLogArgument = (reason: unknown): string | object =>
  typeof reason === "object" && reason !== null ? reason : String(reason);

const reportLoadFailure = (reason: unknown) => {
  try {
    console.error("Monaco initialization: error:", reason);
    getRollbar({ service: SERVICE_NAME }).error("Monaco initialization failed", toLogArgument(reason));
  } catch {
    // Reporting is best effort: a broken console or Rollbar client must not leave an unhandled
    // rejection or break the editor.
  }
};

/** A cancellation is the expected end of an unmounted load; anything else is a real failure. */
const handleLoadRejection = (reason: unknown) => {
  if (!isCancelation(reason)) reportLoadFailure(reason);
};

/**
 * The Monaco instance, or `null` until it has loaded. A drop-in for the hook that
 * `@monaco-editor/react` ships, which cancels its pending load on unmount but never handles the
 * rejection that `cancel()` causes, so every editor unmounted mid-load left an uncaught
 * `{ type: "cancelation" }` behind. Here the cancellation is expected and ignored, while any
 * other rejection is a real load failure (a broken `/monaco/vs` deploy) and is still reported.
 *
 * Once Monaco is cached the instance is returned on the first render and `loader.init()` is not
 * called; otherwise it is called once per mount.
 */
export const useMonacoInstance = (): Monaco | null => {
  const [monaco, setMonaco] = useState<Monaco | null>(() => loader.__getMonacoInstance());

  useEffect(() => {
    if (monaco) return;
    let active = true;
    const pending = loader.init();
    pending
      .then((instance) => {
        if (active) setMonaco(instance);
      })
      .catch(handleLoadRejection);
    return () => {
      active = false;
      pending.cancel();
    };
  }, [monaco]);

  return monaco;
};
