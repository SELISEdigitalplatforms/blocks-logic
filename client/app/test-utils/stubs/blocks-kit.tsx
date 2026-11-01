/**
 * Test-only stub for `@seliseblocks/genesis-os` (and its `/hooks`, `/providers`
 * subpaths).
 *
 * The real package's barrel eagerly imports framer-motion, whose `motion-utils`
 * reads `process.env.NODE_ENV` at import time and throws under the jsdom test
 * environment. Aliasing the package to this stub keeps the heavy design-system
 * (and its animation deps) out of the test module graph while still providing
 * the handful of exports the app modules under test rely on.
 */
import React from "react";
import { create } from "zustand";

export type RuntimeKey = string;

/**
 * Runtime env accessor. The app reads deployment URLs through this; in tests it
 * simply returns an empty string so `getRuntimeEnv(...) || ""` stays well-defined.
 */
export const getRuntimeEnv = (_key?: RuntimeKey): string => "";

/**
 * Mirrors the SDK helper: a project carrying its own custom domain resolves to that app's
 * `blocksapi.` host; anything else falls back to the shared public host, which is unset here.
 */
export const getProjectBlocksApiUrl = (project?: { customDomain?: string | null } | null): string =>
  project?.customDomain ? `blocksapi.${project.customDomain}` : "";

type HttpClientOptions = {
  baseURL?: string;
  blocksKey?: string;
  onError?: (...args: unknown[]) => unknown;
};

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

// Cross-app redirect prefetch stand-in. Reports "ready" with a no-op redirect so
// components that gate a button on `isReady` render it enabled in tests.
export const usePrefetchRedirect = (_options?: unknown) => ({
  isFetching: false,
  isReady: true,
  redirect: () => {},
});

// Theme hook stand-in.
export const useTheme = () => ({
  theme: "light",
  resolvedTheme: "light",
  setTheme: () => {},
});

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
export const RollbarProvider = passthrough("RollbarProvider");

export const getRollbar = (_options?: { service?: string }) => ({});

export const createHttpFailureReporter =
  (_rollbar?: unknown) =>
  (..._args: unknown[]) => {};

export const attachQueryErrorReporting = (
  _queryClient?: unknown,
  _rollbar?: unknown,
) => {};

// ---- i18n runtime (same API as genesis-os; English comes from registerTranslations) ----
type Dictionary = Readonly<Record<string, string>>;
type TranslateValues = Readonly<Record<string, string | number>>;

export const useAppSettingsStore = create<{
  settings: { language?: string };
  setSettings: (settings: { language?: string }) => void;
}>()((set) => ({
  settings: { language: "en" },
  setSettings: (settings) => set((state) => ({ settings: { ...state.settings, ...settings } })),
}));

export const useI18nStore = create<{
  bundled: Record<string, Dictionary>;
  remote: Record<string, Dictionary>;
  registerBundle: (module: string, dictionary: Dictionary) => void;
  setRemote: (language: string, module: string, dictionary: Dictionary) => void;
}>()((set) => ({
  bundled: {},
  remote: {},
  registerBundle: (module, dictionary) =>
    set((state) => ({
      bundled: { ...state.bundled, [module]: { ...state.bundled[module], ...dictionary } },
    })),
  setRemote: (language, module, dictionary) =>
    set((state) => ({ remote: { ...state.remote, [`${language}:${module}`]: dictionary } })),
}));

export const registerTranslations = (module: string, dictionary: Dictionary) => {
  useI18nStore.getState().registerBundle(module, dictionary);
};

const lookup = (language: string, module: string, key: string, values?: TranslateValues) => {
  const at = key.indexOf(":");
  const target = at > 0 ? { module: key.slice(0, at), key: key.slice(at + 1) } : { module, key };
  const { bundled, remote } = useI18nStore.getState();
  const sources = [
    ...(language === "en" ? [] : [remote[`${language}:${target.module}`]]),
    bundled[target.module],
  ];
  const count = values?.count;
  const rules = new Intl.PluralRules(language);
  const keys =
    typeof count === "number"
      ? [`${target.key}_${rules.select(count)}`, `${target.key}_other`, target.key]
      : [target.key];
  for (const source of sources) {
    for (const candidate of keys) {
      const value = source?.[candidate];
      if (value) {
        return value.replaceAll(/\{\{\s*(\w+)\s*\}\}/g, (match, name: string) =>
          values && Object.hasOwn(values, name) ? String(values[name]) : match,
        );
      }
    }
  }
  return target.key;
};

export const tStatic = (module: string, key: string, values?: TranslateValues) =>
  lookup(useAppSettingsStore.getState().settings.language ?? "en", module, key, values);

export const useT = (module: string) => {
  const language = useAppSettingsStore((state) => state.settings.language) ?? "en";
  useI18nStore((state) => state.bundled);
  const remote = useI18nStore((state) => state.remote);
  const t = React.useCallback(
    (key: string, values?: TranslateValues) => lookup(language, module, key, values),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [language, module, remote],
  );
  return { t, language };
};
