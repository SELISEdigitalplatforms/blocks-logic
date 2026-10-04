export type PathSegment = string | number; // key or array index

export type FieldReferenceTarget =
  | { kind: "expression" }
  | { kind: "code"; mode: "all" | "each" };

export type FieldReference = {
  segments: PathSegment[]; // relative to the item output; [] = whole item
  nodeName: string;
  isDirectParent: boolean;
  itemIndex: number;
  itemIsObject: boolean;
};

export const EXPRESSION_TARGET: FieldReferenceTarget = { kind: "expression" };

const IDENTIFIER = /^[A-Za-z_$][\w$]*$/;

// Expression syntax: `.key` / `[n]`, keys are never quoted.
const renderExpressionPath = (segments: PathSegment[]): string =>
  segments.map((segment) => (typeof segment === "number" ? `[${segment}]` : `.${segment}`)).join("");

// JavaScript: `.key` for identifiers, `["odd key"]` otherwise, `[n]` for indexes.
const renderCodePath = (segments: PathSegment[]): string =>
  segments
    .map((segment) => {
      if (typeof segment === "number") return `[${segment}]`;
      return IDENTIFIER.test(segment) ? `.${segment}` : `[${JSON.stringify(segment)}]`;
    })
    .join("");

const buildCodePrefix = (ref: FieldReference, mode: "all" | "each"): string => {
  const node = `$node[${JSON.stringify(ref.nodeName)}]`;

  if (mode === "each") {
    if (ref.isDirectParent) return ref.itemIsObject ? "$json" : "$json.json";
    return ref.itemIsObject ? `${node}.json` : `${node}.json.value`;
  }

  const base = ref.isDirectParent ? `$items[${ref.itemIndex}]` : `${node}.item(${ref.itemIndex})`;
  return ref.itemIsObject ? `${base}.json` : `${base}.json.value`;
};

export function buildFieldReference(ref: FieldReference, target: FieldReferenceTarget): string {
  if (target.kind === "code") {
    return buildCodePrefix(ref, target.mode) + renderCodePath(ref.segments);
  }

  const path = renderExpressionPath(ref.segments);
  return ref.isDirectParent
    ? `{{$json.output${path}}}`
    : `{{$node["${ref.nodeName}"].json.output${path}}}`;
}

// Matches the server: only a non-array object output is exposed as the item's `json` directly.
export const isPlainObject = (value: unknown): boolean =>
  typeof value === "object" && value !== null && !Array.isArray(value);
