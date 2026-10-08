import { loader } from "@monaco-editor/react";

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
