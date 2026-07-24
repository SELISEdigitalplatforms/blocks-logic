/**
 * Test-only stub for `@seliseblocks/blocks-kit` (and its `/hooks`, `/providers`
 * subpaths).
 *
 * The real package's barrel eagerly imports framer-motion, whose `motion-utils`
 * reads `process.env.NODE_ENV` at import time and throws under the jsdom test
 * environment. Aliasing the package to this stub keeps the heavy design-system
 * (and its animation deps) out of the test module graph while still providing
 * the handful of exports the app modules under test rely on.
 */
import React from "react";

export type RuntimeKey = string;

/**
 * Runtime env accessor. The app reads deployment URLs through this; in tests it
 * simply returns an empty string so `getRuntimeEnv(...) || ""` stays well-defined.
 */
export const getRuntimeEnv = (_key?: RuntimeKey): string => "";

type HttpClientOptions = { baseURL?: string; blocksKey?: string };

/**
 * Minimal HttpClient stand-in. Service tests mock `@/lib/http-client` or the
 * service modules directly, so these methods are rarely invoked; they exist so
 * constructing the client at module load time does not throw.
 */
export class HttpClient {
  baseURL: string;
  blocksKey: string;

  constructor(options: HttpClientOptions = {}) {
    this.baseURL = options.baseURL ?? "";
    this.blocksKey = options.blocksKey ?? "";
  }

  get<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  post<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  put<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  patch<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  delete<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
  stream<T = unknown>(): Promise<T> {
    return Promise.resolve(undefined as T);
  }
}

export class HttpError extends Error {
  status: number;
  errors: Record<string, string | string[]>;
  constructor(status = 0, errors: Record<string, string | string[]> = {}) {
    super(`HttpError ${status}`);
    this.status = status;
    this.errors = errors;
  }
}

// Returns a path scoper; the identity keeps navigation targets predictable.
export const useScopedPath = () => (path: string) => path;

// Theme hook stand-in.
export const useTheme = () => ({
  theme: "light",
  resolvedTheme: "light",
  setTheme: () => {},
});

// Project store stand-in. The real export is a zustand store, so it is both a
// hook (callable) and carries `getState`/`setState`. Schema `transform` and
// `onChange` handlers read `useProjectStore.getState().selectedProject`.
type ProjectState = {
  selectedProject: {
    tenantId: string;
    tenantSlug?: string;
    projectKey: string;
  } | null;
};

let projectState: ProjectState = {
  selectedProject: { tenantId: "", tenantSlug: "", projectKey: "" },
};

export const useProjectStore = Object.assign(() => projectState, {
  getState: () => projectState,
  setState: (partial: Partial<ProjectState>) => {
    projectState = { ...projectState, ...partial };
  },
});

type PassthroughProps = { children?: React.ReactNode } & Record<string, unknown>;

const passthrough = (label: string) => {
  const Component = ({ children }: PassthroughProps) =>
    React.createElement(React.Fragment, null, children);
  Component.displayName = label;
  return Component;
};

// Layout component rendered as a transparent passthrough.
export const BlocksAppLayout = passthrough("BlocksAppLayout");
