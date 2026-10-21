import type { Edge } from "@xyflow/react";
import type { NodeHandle } from "../models/node.model";

/** Stable output key prefix; the server accepts `^switch-handle-source-[a-z0-9]{6}$`. */
export const SWITCH_KEY_PREFIX = "switch-handle-source-";
/** Reserved key of the Rules-mode "Extra output" fallback handle. */
export const SWITCH_FALLBACK_KEY = "switch-handle-source-fallback";

const KEY_FORMAT = /^switch-handle-source-[a-z0-9]{6}$/;
const ALPHABET = "abcdefghijklmnopqrstuvwxyz0123456789";

export type SwitchMode = "rules" | "expression";

export interface SwitchCondition {
  left?: string;
  operator?: string;
  right?: string;
  type?: string;
}

export interface SwitchRule {
  key: string;
  outputName: string;
  conditionType: string;
  conditions: SwitchCondition[];
}

export interface SwitchOutput {
  key: string;
  outputName: string;
}

export interface SwitchOptions {
  fallbackOutput: string;
  renameFallbackOutput: string;
  ignoreCase: boolean;
  allMatchingOutputs: boolean;
}

export interface SwitchParameters {
  mode: SwitchMode;
  rules: SwitchRule[];
  outputs: SwitchOutput[];
  output: string;
  options: SwitchOptions;
}

export const isSwitchKey = (key: unknown): key is string => typeof key === "string" && KEY_FORMAT.test(key);

const randomSuffix = (): string => {
  const bytes = new Uint8Array(6);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => ALPHABET[b % ALPHABET.length]).join("");
};

/** A new output key that none of `used` already has. */
export const generateSwitchKey = (used: Iterable<string>): string => {
  const taken = new Set(used);
  let key = `${SWITCH_KEY_PREFIX}${randomSuffix()}`;
  while (taken.has(key)) key = `${SWITCH_KEY_PREFIX}${randomSuffix()}`;
  return key;
};

/** Every key used by a rule or output of the node (both lists), so a new key is unique within the node. */
export const usedSwitchKeys = (parameters: Partial<SwitchParameters> | undefined): string[] => [
  ...asArray<SwitchRule>(parameters?.rules).map((r) => r.key),
  ...asArray<SwitchOutput>(parameters?.outputs).map((o) => o.key),
];

const asArray = <T,>(value: unknown): T[] => (Array.isArray(value) ? (value.filter(Boolean) as T[]) : []);

const asText = (value: unknown): string => (typeof value === "string" ? value : "");

export const switchMode = (parameters: Partial<SwitchParameters> | Record<string, unknown> | undefined): SwitchMode =>
  (parameters as Partial<SwitchParameters> | undefined)?.mode === "expression" ? "expression" : "rules";

/**
 * The node's handle, derived from its parameters (the source of truth). Stored labels are the raw
 * output names, possibly ""; see {@link switchHandleLabel} for what is displayed.
 */
export const deriveSwitchHandle = (raw: Record<string, unknown> | undefined): NodeHandle => {
  const parameters = (raw ?? {}) as Partial<SwitchParameters>;
  const targets = [{ key: "target", label: "" }];
  if (switchMode(parameters) === "expression") {
    return {
      targets,
      sources: asArray<SwitchOutput>(parameters.outputs).map((o) => ({ key: o.key, label: asText(o.outputName) })),
    };
  }
  const sources = asArray<SwitchRule>(parameters.rules).map((r) => ({ key: r.key, label: asText(r.outputName) }));
  if (parameters.options?.fallbackOutput === "extra") {
    sources.push({ key: SWITCH_FALLBACK_KEY, label: asText(parameters.options.renameFallbackOutput) });
  }
  return { targets, sources };
};

/**
 * What a Switch handle or edge shows: its name, or "Output <i>" by position in its list; the fallback
 * shows its name or "Fallback".
 */
export const switchHandleLabel = (handle: NodeHandle | null | undefined, key: string | null | undefined): string => {
  if (!key) return "";
  const sources = handle?.sources ?? [];
  const index = sources.findIndex((entry) => entry.key === key);
  if (index < 0) return "";
  const label = sources[index].label;
  if (label) return label;
  return key === SWITCH_FALLBACK_KEY ? "Fallback" : `Output ${index}`;
};

export const newSwitchRule = (used: Iterable<string>): SwitchRule => ({
  key: generateSwitchKey(used),
  outputName: "",
  conditionType: "all",
  conditions: [],
});

export const newSwitchOutput = (used: Iterable<string>): SwitchOutput => ({
  key: generateSwitchKey(used),
  outputName: "",
});

/** Fresh default parameters for a new Switch: one empty rule with a new key. Never shared between nodes. */
export const defaultSwitchParameters = (): SwitchParameters => ({
  mode: "rules",
  rules: [newSwitchRule([])],
  outputs: [],
  output: "",
  options: { fallbackOutput: "none", renameFallbackOutput: "", ignoreCase: false, allMatchingOutputs: false },
});

/** Edges of `nodeId` on source keys that are not in `keys`. */
export const edgesOffKeys = (edges: Iterable<Edge>, nodeId: string, keys: Iterable<string>): Edge[] => {
  const kept = new Set(keys);
  return Array.from(edges).filter((e) => e.source === nodeId && !kept.has(e.sourceHandle ?? ""));
};
