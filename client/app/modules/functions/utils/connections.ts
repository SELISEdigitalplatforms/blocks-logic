import {
  CONNECTION_PRESETS,
  ConnectionPrefills,
  IConnectionPreset,
  IConnectionVariable,
} from "../constants/connections.constant";
import { IVariableBinding } from "../types/function.types";

/** Node's built-in modules — importable without an entry in package.json, `node:` prefix or not. */
const NODE_BUILTINS = new Set([
  "assert",
  "async_hooks",
  "buffer",
  "child_process",
  "cluster",
  "console",
  "constants",
  "crypto",
  "dgram",
  "diagnostics_channel",
  "dns",
  "domain",
  "events",
  "fs",
  "http",
  "http2",
  "https",
  "inspector",
  "module",
  "net",
  "os",
  "path",
  "perf_hooks",
  "process",
  "punycode",
  "querystring",
  "readline",
  "repl",
  "stream",
  "string_decoder",
  "sys",
  "timers",
  "tls",
  "trace_events",
  "tty",
  "url",
  "util",
  "v8",
  "vm",
  "wasi",
  "worker_threads",
  "zlib",
]);

const IMPORT_PATTERNS = [
  // import x from "a"; import { x } from "a"; import * as x from "a"; import "a"
  /\bimport\s+(?:[\w$*{}\s,]+\s+from\s+)?["']([^"'\n]+)["']/g,
  // export { x } from "a"; export * from "a"
  /\bexport\s+[\w$*{}\s,]+\s+from\s+["']([^"'\n]+)["']/g,
  // await import("a")
  /\bimport\(\s*["']([^"'\n]+)["']\s*\)/g,
  // require("a") — createRequire in an ES module
  /\brequire\(\s*["']([^"'\n]+)["']\s*\)/g,
];

/**
 * Drops comments so a commented-out import is not reported. Line comments are only taken where
 * `//` is not preceded by `:`, which keeps `"https://…"` strings intact.
 */
const stripComments = (source: string) =>
  source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:\\])\/\/.*$/gm, "$1");

/** `@scope/name/sub` → `@scope/name`, `name/sub` → `name`; null for anything npm does not install. */
export const toPackageName = (specifier: string): string | null => {
  const spec = specifier.trim();
  if (!spec || spec.startsWith(".") || spec.startsWith("/") || /^[a-z]+:/i.test(spec)) return null;
  const parts = spec.split("/");
  const name = spec.startsWith("@")
    ? parts.length >= 2
      ? `${parts[0]}/${parts[1]}`
      : null
    : parts[0];
  if (!name || NODE_BUILTINS.has(name)) return null;
  return name;
};

/** npm packages the source imports, de-duplicated, in first-seen order. */
export const findImportedPackages = (source: string): string[] => {
  const code = stripComments(source);
  const found: string[] = [];
  for (const pattern of IMPORT_PATTERNS) {
    for (const match of code.matchAll(pattern)) {
      const name = toPackageName(match[1]);
      if (name && !found.includes(name)) found.push(name);
    }
  }
  return found;
};

/** Keys read from `ctx.env` — dotted, bracketed with a literal, or destructured. */
export const findEnvKeys = (source: string): string[] => {
  const code = stripComments(source);
  const found: string[] = [];
  const push = (key: string) => {
    if (key && !found.includes(key)) found.push(key);
  };
  for (const match of code.matchAll(/\bctx\.env\.([A-Za-z_$][\w$]*)/g)) push(match[1]);
  for (const match of code.matchAll(/\bctx\.env\[\s*["']([^"'\n]+)["']\s*\]/g)) push(match[1]);
  for (const match of code.matchAll(/\{([^{}]*)\}\s*=\s*ctx\.env\b/g)) {
    for (const part of match[1].split(",")) {
      // `A`, `A: alias`, `A = "default"` — the key is always the leading identifier.
      const key = /^\s*([A-Za-z_$][\w$]*)/.exec(part)?.[1];
      if (key) push(key);
    }
  }
  return found;
};

type Manifest = Record<string, unknown>;

const isPlainObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

/** The manifest as an object, or null when it is not valid JSON or not an object. */
export const parseManifest = (packageJson: string): Manifest | null => {
  try {
    const parsed: unknown = JSON.parse(packageJson);
    return isPlainObject(parsed) ? parsed : null;
  } catch {
    return null;
  }
};

/** `dependencies` as a name → range map. Anything malformed reads as empty. */
export const getDependencies = (packageJson: string): Record<string, string> => {
  const deps = parseManifest(packageJson)?.dependencies;
  if (!isPlainObject(deps)) return {};
  return Object.fromEntries(
    Object.entries(deps).filter((entry): entry is [string, string] => typeof entry[1] === "string"),
  );
};

export const presetForPackage = (name: string): IConnectionPreset | undefined =>
  CONNECTION_PRESETS.find((preset) => preset.packageName === name);

export type PresetStatus = "added" | "partial" | "none";

export const presetStatus = (
  preset: IConnectionPreset,
  dependencies: Record<string, string>,
  variables: IVariableBinding[],
): PresetStatus => {
  const keys = new Set(variables.map((variable) => variable.key));
  const parts = [
    preset.packageName in dependencies,
    ...preset.variables.map((variable) => keys.has(variable.key)),
  ];
  if (parts.every(Boolean)) return "added";
  return parts.some(Boolean) ? "partial" : "none";
};

export type ApplyPresetResult =
  | { ok: false; reason: string }
  | {
      ok: true;
      packageJson: string;
      variables: IVariableBinding[];
      addedPackage: boolean;
      /** Set when the package was already listed; that range is left as the user chose it. */
      existingVersion?: string;
      addedKeys: string[];
      /** Existing blank rows given a known value (the project's host, a default queue name). */
      filledKeys: string[];
      /** The version pinned when the package was added. */
      version: string;
    };

const EXACT_VERSION = /^\d+\.\d+\.\d+$/;
const NPM_REGISTRY = "https://registry.npmjs.org";
const LATEST_TIMEOUT_MS = 4000;

export type ResolvedVersion = { version: string; source: "npm" | "fallback" };

/**
 * The package's current `latest` release on npm, so a connection starts on today's version rather
 * than whatever was current when the catalog was written. It is still written into package.json
 * as an exact version: builds install fresh with no lockfile and cache on the manifest text, so a
 * floating `latest` would both freeze at the first build and jump majors on an unrelated rebuild.
 *
 * Falls back to the catalog's tested version when npm is unreachable, slow, or answers with
 * anything but a plain release — a pre-release or malformed tag is never pinned.
 */
export const resolveLatestVersion = async (
  preset: IConnectionPreset,
  fetchImpl: typeof fetch = globalThis.fetch?.bind(globalThis),
): Promise<ResolvedVersion> => {
  const fallback: ResolvedVersion = { version: preset.version, source: "fallback" };
  if (!fetchImpl) return fallback;
  try {
    const name = preset.packageName.replace("/", "%2F");
    const response = await fetchImpl(`${NPM_REGISTRY}/${name}/latest`, {
      headers: { accept: "application/json" },
      signal: AbortSignal.timeout(LATEST_TIMEOUT_MS),
    });
    if (!response.ok) return fallback;
    const body: unknown = await response.json();
    const version = isPlainObject(body) ? body.version : undefined;
    return typeof version === "string" && EXACT_VERSION.test(version)
      ? { version, source: "npm" }
      : fallback;
  } catch {
    return fallback;
  }
};

const toParts = (version: string | undefined): number[] | null =>
  version !== undefined && EXACT_VERSION.test(version) ? version.split(".").map(Number) : null;

/**
 * Orders two exact `x.y.z` versions (negative when `a` is older). Null when either is not one — a
 * range, a tag or a pre-release is the user's own choice and is never compared or replaced.
 */
export const compareVersions = (a: string | undefined, b: string | undefined): number | null => {
  const left = toParts(a);
  const right = toParts(b);
  if (!left || !right) return null;
  for (let i = 0; i < 3; i++) if (left[i] !== right[i]) return left[i] - right[i];
  return 0;
};

export type VersionUpdate = {
  from: string;
  to: string;
  /** Semver says it can break the code: a new major, or a new minor while still on 0.x. */
  breaking: boolean;
};

/** A newer stable release than the exact version pinned, or null when there is nothing to offer. */
export const findUpdate = (
  pinned: string | undefined,
  latest: string | undefined,
): VersionUpdate | null => {
  const order = compareVersions(pinned, latest);
  if (order === null || order >= 0) return null;
  const [fromMajor, fromMinor] = toParts(pinned)!;
  const [toMajor, toMinor] = toParts(latest)!;
  return {
    from: pinned!,
    to: latest!,
    breaking: fromMajor !== toMajor || (fromMajor === 0 && fromMinor !== toMinor),
  };
};

/**
 * Re-pins a package that package.json already lists to another exact version. Refuses rather than
 * rewrites when the manifest does not parse, the package is gone, or the version is not exact.
 */
export const pinVersion = (
  packageJson: string,
  packageName: string,
  version: string,
): { ok: true; packageJson: string } | { ok: false; reason: string } => {
  if (!EXACT_VERSION.test(version))
    return { ok: false, reason: `${version} is not an exact version.` };
  const manifest = parseManifest(packageJson);
  if (!manifest) {
    return { ok: false, reason: "package.json is not valid JSON — fix it first, then update." };
  }
  const dependencies = manifest.dependencies;
  if (!isPlainObject(dependencies) || typeof dependencies[packageName] !== "string") {
    return { ok: false, reason: `${packageName} is no longer in package.json.` };
  }
  return {
    ok: true,
    packageJson: `${JSON.stringify(
      { ...manifest, dependencies: { ...dependencies, [packageName]: version } },
      null,
      2,
    )}\n`,
  };
};

/**
 * What a preset variable can start as: the project's own value when the console knows it, else the
 * catalog default. A secret has neither, so it is always left for the user to bind.
 */
const knownValue = (variable: IConnectionVariable, prefills: ConnectionPrefills): string =>
  variable.secret
    ? ""
    : (variable.prefill && prefills[variable.prefill]?.trim()) || variable.defaultValue || "";

const isBlank = (binding: IVariableBinding) => binding.value.trim() === "";

/**
 * Fills blank variables the catalog knows a plain value for (`BLOCKS_API_URL` from the project's
 * host, a default queue name). Anything typed or bound is kept, and secrets are never filled.
 */
export const fillKnownValues = (
  variables: IVariableBinding[],
  prefills: ConnectionPrefills = {},
): { variables: IVariableBinding[]; filledKeys: string[] } => {
  const catalog = new Map(
    CONNECTION_PRESETS.flatMap((preset) => preset.variables).map((v) => [v.key, v] as const),
  );
  const filledKeys: string[] = [];
  const next = variables.map((binding) => {
    const variable = catalog.get(binding.key);
    const value = variable && isBlank(binding) ? knownValue(variable, prefills) : "";
    if (!value) return binding;
    filledKeys.push(binding.key);
    return { ...binding, value };
  });
  return { variables: next, filledKeys };
};

/**
 * Adds the preset's package (pinned) and any of its variables the function does not have yet.
 * Existing entries are never overwritten, and a manifest that does not parse is refused rather
 * than replaced — the user's text is the source of truth, and silently rewriting it would lose it.
 */
export const applyPreset = (
  preset: IConnectionPreset,
  packageJson: string,
  variables: IVariableBinding[],
  prefills: ConnectionPrefills = {},
  /** Exact version to pin; the catalog's own when omitted. */
  version: string = preset.version,
): ApplyPresetResult => {
  const manifest = parseManifest(packageJson);
  if (!manifest) {
    return {
      ok: false,
      reason: "package.json is not valid JSON — fix it first, then add the connection.",
    };
  }
  if (manifest.dependencies !== undefined && !isPlainObject(manifest.dependencies)) {
    return { ok: false, reason: `"dependencies" in package.json must be an object.` };
  }

  const dependencies = (manifest.dependencies ?? {}) as Record<string, unknown>;
  const existing = dependencies[preset.packageName];
  const addedPackage = existing === undefined;
  const nextPackageJson = addedPackage
    ? `${JSON.stringify(
        { ...manifest, dependencies: { ...dependencies, [preset.packageName]: version } },
        null,
        2,
      )}\n`
    : packageJson;

  const keys = new Set(variables.map((variable) => variable.key));
  const added = preset.variables.filter((variable) => !keys.has(variable.key));
  const addedKeys = added.map((variable) => variable.key);
  // A row this preset declares but the user left blank gets the same known value a new row would.
  const filledKeys: string[] = [];
  const rows = variables.map((binding) => {
    const variable = preset.variables.find((v) => v.key === binding.key);
    const value = variable && isBlank(binding) ? knownValue(variable, prefills) : "";
    if (!value) return binding;
    filledKeys.push(binding.key);
    return { ...binding, value };
  });

  return {
    ok: true,
    packageJson: nextPackageJson,
    variables: [
      ...rows,
      ...added.map((variable) => ({ key: variable.key, value: knownValue(variable, prefills) })),
    ],
    addedPackage,
    version,
    existingVersion: addedPackage ? undefined : String(existing),
    addedKeys,
    filledKeys,
  };
};

export interface SetupCheck {
  manifestValid: boolean;
  /** Imported in index.js but missing from package.json — the build would fail to resolve them. */
  missingPackages: string[];
  /** Read from ctx.env but not defined — they would arrive as undefined. */
  missingVariables: string[];
  /** Defined with no value and not bound — they would arrive as an empty string. */
  emptyVariables: string[];
}

export const checkSetup = (
  indexJs: string,
  packageJson: string,
  variables: IVariableBinding[],
): SetupCheck => {
  const manifestValid = parseManifest(packageJson) !== null;
  const dependencies = getDependencies(packageJson);
  const keys = new Set(variables.map((variable) => variable.key));
  return {
    manifestValid,
    // With an unreadable manifest every import would look missing; the manifest error says it.
    missingPackages: manifestValid
      ? findImportedPackages(indexJs).filter((name) => !(name in dependencies))
      : [],
    missingVariables: findEnvKeys(indexJs).filter((key) => !keys.has(key)),
    emptyVariables: variables
      .filter((variable) => variable.key && variable.value.trim() === "")
      .map((variable) => variable.key),
  };
};

/** Appends empty rows for keys the code reads but the function does not define. */
export const addMissingVariables = (
  variables: IVariableBinding[],
  missing: string[],
): IVariableBinding[] => {
  const keys = new Set(variables.map((variable) => variable.key));
  const toAdd = missing.filter((key, index) => !keys.has(key) && missing.indexOf(key) === index);
  return [...variables, ...toAdd.map((key) => ({ key, value: "" }))];
};
