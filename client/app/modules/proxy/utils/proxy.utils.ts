import { z } from "zod";
import { ProxyExecutionLog, ProxyFormValues, ResponseFieldNode } from "../types";

export const slugifyProxyName = (name: string) =>
  name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80);

/**
 * Configuration-variable token: the literal `{{$VAR.` prefix, a name in `[A-Za-z0-9._:-]`, then
 * `}}`. Mirrors the server regex in `Proxy.DomainService/Utils/ProxyVarRef.cs` — case-sensitive,
 * so `{{ $VAR.x }}` / `{{$var.x}}` / `${SECRET.X}` are literal text, not tokens.
 */
export const VAR_REF_RE = /\{\{\$VAR\.[A-Za-z0-9._:-]+\}\}/;

/** True when `value` contains at least one `{{$VAR.name}}` token. */
export const containsVarRef = (value: string) => VAR_REF_RE.test(value);

/** The token literal for a variable name, e.g. `buildVarToken("api-key") === "{{$VAR.api-key}}"`. */
export const buildVarToken = (name: string) => `{{$VAR.${name}}}`;

/** Inserts `token` into `value` at `caret` (clamped); a caret past the end appends. */
export const insertToken = (value: string, caret: number, token: string) => {
  const at = Math.max(0, Math.min(caret, value.length));
  return value.slice(0, at) + token + value.slice(at);
};

export const maskUpstreamUrl = (url: string) => {
  try {
    const parsed = new URL(url);
    const parts = parsed.pathname.split("/").filter(Boolean);
    const last = parts.at(-1);
    return last ? `${parsed.origin}/${parts[0] ?? ""}/.../${last}` : parsed.origin;
  } catch {
    return url;
  }
};

const keyValueSchema = z
  .array(
    z.object({
      key: z.string(),
      value: z.string(),
    }),
  )
  .superRefine((rows, ctx) => {
    const blankRows = rows.filter((row) => !row.key.trim() && !row.value.trim());
    if (blankRows.length > 1) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Remove duplicate blank rows.",
      });
    }

    rows.forEach((row, index) => {
      const hasAnyValue = row.key.trim() || row.value.trim();
      if (hasAnyValue && !row.key.trim()) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: [index, "key"],
          message: "Key is required when a value is provided.",
        });
      }
    });
  });

const methodOverrideSchema = z.object({
  method: z.enum(["GET", "POST", "PUT", "PATCH", "DELETE"]),
  upstream: z.string().nullable(),
  headers: keyValueSchema.nullable(),
  query: keyValueSchema.nullable(),
});

// ---------------------------------------------------------------------------
// Response field filtering ("Send everything" vs "Choose fields")
// Mirrors server/Proxy.DomainService/Utils/ProxyResponsePath.cs +
// ProxyResponseProjector.cs. The path grammar, the caps, and the projector's
// M1–M5 keep rules are re-implemented here for the console preview only.
// ---------------------------------------------------------------------------

/** `segment ( "." segment )*` where `segment = KEY [ "[]" ]`, `KEY = [^.\[\]]+`. Mirrors the server regex. */
export const RESPONSE_PATH_RE = /^(?:[^.[\]]+(?:\[\])?)(?:\.[^.[\]]+(?:\[\])?)*$/;
export const MAX_RESPONSE_PATH_SEGMENTS = 25;
export const MAX_RESPONSE_PATHS = 200;
/** Mirrors `ProxyResponsePath.MaxPathLength` — the longest one stored path expression may be. */
export const MAX_RESPONSE_PATH_LENGTH = 512;
/** Mirrors `ProxyResponseProjector.MaxProjectableBytes`. */
export const MAX_PROJECTABLE_BYTES = 5 * 1024 * 1024;
/** Array elements unioned per level when deriving a schema from a Test sample. */
export const RESPONSE_SCHEMA_SAMPLE = 50;

/** trim → grammar → ≤ {@link MAX_RESPONSE_PATH_SEGMENTS} segments. */
export const isResponsePath = (value: string): boolean => {
  const trimmed = value.trim();
  if (!trimmed || trimmed.length > MAX_RESPONSE_PATH_LENGTH || !RESPONSE_PATH_RE.test(trimmed))
    return false;
  return trimmed.split(".").length <= MAX_RESPONSE_PATH_SEGMENTS;
};

let responseNodeSeq = 0;
const responseNodeUid = () => `rf-${Date.now().toString(36)}-${(responseNodeSeq++).toString(36)}`;

const isPlainObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

const splitSegment = (raw: string): { key: string; isList: boolean } => {
  const isList = raw.endsWith("[]");
  return { key: isList ? raw.slice(0, -2) : raw, isList };
};

const encodeSegment = (node: ResponseFieldNode) =>
  `${node.key.trim()}${node.isList ? "[]" : ""}`;

/**
 * Minimal-encode a tree + checked-id set to a deduped path list: emit the path of every checked node
 * that has NO checked ancestor; skip empty-key nodes (and anything below one — it cannot form a
 * valid path).
 */
export const treeToPaths = (nodes: ResponseFieldNode[], checked: Set<string>): string[] => {
  const out: string[] = [];
  const walk = (list: ResponseFieldNode[], prefix: string[], checkedAncestor: boolean) => {
    for (const node of list) {
      const key = node.key.trim();
      if (!key) continue; // unnamed row — not encodable, and neither is anything under it
      const path = [...prefix, encodeSegment(node)];
      const selfChecked = checked.has(node.id);
      if (selfChecked && !checkedAncestor) out.push(path.join("."));
      walk(node.children, path, checkedAncestor || selfChecked);
    }
  };
  walk(nodes, [], false);
  return [...new Set(out)];
};

/**
 * Build a scaffold tree from saved paths. Every leaf-of-path node is checked; the intermediate nodes
 * it passes through are present but unchecked. `key[]` → `isList`.
 */
export const pathsToTree = (
  paths: string[],
): { tree: ResponseFieldNode[]; checked: Set<string> } => {
  const tree: ResponseFieldNode[] = [];
  const checked = new Set<string>();
  for (const raw of paths) {
    if (!isResponsePath(raw)) continue;
    const segments = raw.trim().split(".");
    let siblings = tree;
    segments.forEach((segRaw, index) => {
      const { key, isList } = splitSegment(segRaw);
      let node = siblings.find((sibling) => sibling.key === key);
      if (!node) {
        node = { id: responseNodeUid(), key, isList, children: [] };
        siblings.push(node);
      } else if (isList) {
        node.isList = true;
      }
      if (index === segments.length - 1) checked.add(node.id);
      siblings = node.children;
    });
  }
  return { tree, checked };
};

const unionObjectKeys = (objects: Record<string, unknown>[]): string[] => {
  const keys: string[] = [];
  const seen = new Set<string>();
  for (const object of objects.slice(0, RESPONSE_SCHEMA_SAMPLE)) {
    for (const key of Object.keys(object)) {
      if (!seen.has(key)) {
        seen.add(key);
        keys.push(key);
      }
    }
  }
  return keys;
};

const schemaNodesFromObject = (
  objects: Record<string, unknown>[],
  depth: number,
): ResponseFieldNode[] => {
  if (depth > MAX_RESPONSE_PATH_SEGMENTS) return [];
  return unionObjectKeys(objects).map((key) => {
    const values = objects
      .map((object) => object[key])
      .filter((value) => value !== undefined);
    const objectValues = values.filter(isPlainObject);
    const arrayValues = values.filter((value): value is unknown[] => Array.isArray(value));

    if (arrayValues.length) {
      const elementObjects = arrayValues.flat().filter(isPlainObject);
      return {
        id: responseNodeUid(),
        key,
        isList: true,
        children: elementObjects.length
          ? schemaNodesFromObject(elementObjects, depth + 1)
          : [],
      };
    }

    if (objectValues.length) {
      return {
        id: responseNodeUid(),
        key,
        isList: false,
        children: schemaNodesFromObject(objectValues, depth + 1),
      };
    }

    return { id: responseNodeUid(), key, isList: false, children: [] };
  });
};

/**
 * Read the *shape* of a parsed Test sample. Objects recurse; arrays → `isList`; an array of objects
 * unions keys across the first {@link RESPONSE_SCHEMA_SAMPLE} elements. Arrays of primitives / empty
 * arrays → a childless list leaf. Stops emitting nodes past {@link MAX_RESPONSE_PATH_SEGMENTS} depth.
 * Values are never read.
 */
export const deriveResponseSchema = (
  sample: unknown,
): { rootKind: "object" | "array-of-objects" | "primitive"; tree: ResponseFieldNode[] } => {
  if (isPlainObject(sample)) {
    return { rootKind: "object", tree: schemaNodesFromObject([sample], 1) };
  }
  if (Array.isArray(sample)) {
    const objects = sample.filter(isPlainObject);
    if (!objects.length) return { rootKind: "primitive", tree: [] };
    return { rootKind: "array-of-objects", tree: schemaNodesFromObject(objects, 1) };
  }
  return { rootKind: "primitive", tree: [] };
};

const cloneSchemaSubtree = (
  node: ResponseFieldNode,
  checked: Set<string>,
): ResponseFieldNode => {
  const copy: ResponseFieldNode = {
    id: responseNodeUid(),
    key: node.key,
    isList: node.isList,
    children: node.children.map((child) => cloneSchemaSubtree(child, checked)),
  };
  checked.add(copy.id);
  return copy;
};

const cloneTree = (nodes: ResponseFieldNode[]): ResponseFieldNode[] =>
  nodes.map((node) => ({ ...node, children: cloneTree(node.children) }));

/**
 * Non-destructive overlay: for every schema node, find-or-create the matching node in `tree` (by
 * key path); keep existing checked state; newly created nodes (and everything under them) are added
 * to `checked`.
 */
export const mergeSchemaIntoTree = (
  tree: ResponseFieldNode[],
  checked: Set<string>,
  schema: ResponseFieldNode[],
): { tree: ResponseFieldNode[]; checked: Set<string> } => {
  const nextTree = cloneTree(tree);
  const nextChecked = new Set(checked);

  const merge = (target: ResponseFieldNode[], source: ResponseFieldNode[]) => {
    for (const sourceNode of source) {
      const key = sourceNode.key.trim();
      if (!key) continue;
      let match = target.find((node) => node.key.trim() === key);
      if (!match) {
        match = cloneSchemaSubtree(sourceNode, nextChecked);
        target.push(match);
        continue;
      }
      if (sourceNode.isList) match.isList = true;
      merge(match.children, sourceNode.children);
    }
  };

  merge(nextTree, schema);
  return { tree: nextTree, checked: nextChecked };
};

/** Number of leaf nodes in a schema tree — the count shown in "Filled N fields …". */
export const countResponseLeaves = (nodes: ResponseFieldNode[]): number =>
  nodes.reduce(
    (total, node) => total + (node.children.length ? countResponseLeaves(node.children) : 1),
    0,
  );

const hasCheckedInSubtree = (node: ResponseFieldNode, checked: Set<string>): boolean =>
  checked.has(node.id) || node.children.some((child) => hasCheckedInSubtree(child, checked));

/**
 * Checked subtree → JSON skeleton value: object → `{}`, list → `[oneElement]` (or `[null]`),
 * leaf → `null`. Shape only — every value is `null` / `{}`.
 */
export const treeToSkeleton = (
  nodes: ResponseFieldNode[],
  checked: Set<string>,
): unknown => {
  const buildObject = (list: ResponseFieldNode[]): Record<string, unknown> => {
    const object: Record<string, unknown> = {};
    for (const node of list) {
      const key = node.key.trim();
      if (!key || !hasCheckedInSubtree(node, checked)) continue;
      const childObject = buildObject(node.children);
      const base = Object.keys(childObject).length
        ? childObject
        : checked.has(node.id)
          ? null
          : {};
      object[key] = node.isList ? [base] : base;
    }
    return object;
  };
  return buildObject(nodes);
};

/**
 * Parse a user-edited skeleton (already `JSON.parse`d) to a path list — KEY STRUCTURE ONLY, every
 * value ignored; arrays contribute `[]` to their key and recurse on element 0.
 */
export const skeletonToPaths = (json: unknown): string[] => {
  const out: string[] = [];
  const walk = (node: unknown, segments: string[]) => {
    if (Array.isArray(node)) {
      const segs =
        segments.length && !segments[segments.length - 1].endsWith("[]")
          ? [...segments.slice(0, -1), `${segments[segments.length - 1]}[]`]
          : segments;
      const first = node[0];
      if (isPlainObject(first) || Array.isArray(first)) {
        walk(first, segs);
      } else if (segs.length) {
        out.push(segs.join("."));
      }
      return;
    }
    if (isPlainObject(node)) {
      const keys = Object.keys(node);
      if (!keys.length) {
        if (segments.length) out.push(segments.join("."));
        return;
      }
      for (const key of keys) walk(node[key], [...segments, key]);
      return;
    }
    if (segments.length) out.push(segments.join("."));
  };
  walk(json, []);
  return [...new Set(out)].filter(isResponsePath);
};

/**
 * JS port of the server projector's keep rules (M1–M5) — used only for the "Test sample" preview
 * pane. An empty / all-invalid path list yields `{}` / `[]` (or the primitive itself).
 */
export const projectSample = (sample: unknown, paths: string[]): unknown => {
  const cursors = paths
    .filter(isResponsePath)
    .map((path) => ({
      path: path.trim().split(".").map((segment) => splitSegment(segment).key),
      pos: 0,
    }));

  if (!cursors.length) {
    if (Array.isArray(sample)) return [];
    return isPlainObject(sample) ? {} : sample;
  }

  type Cursor = { path: string[]; pos: number };
  const project = (node: unknown, state: Cursor[]): unknown => {
    if (state.some((cursor) => cursor.pos === cursor.path.length)) return node; // M1

    if (Array.isArray(node)) {
      return node.map((element) =>
        element && typeof element === "object" ? project(element, state) : element,
      );
    }

    if (isPlainObject(node)) {
      const out: Record<string, unknown> = {};
      for (const [key, value] of Object.entries(node)) {
        const next = state
          .filter((cursor) => cursor.pos < cursor.path.length && cursor.path[cursor.pos] === key)
          .map((cursor) => ({ path: cursor.path, pos: cursor.pos + 1 }));
        if (!next.length) continue;
        if (value && typeof value === "object") {
          out[key] = project(value, next);
        } else if (next.some((cursor) => cursor.pos === cursor.path.length)) {
          out[key] = value;
        }
      }
      return out;
    }

    return node;
  };

  return project(sample, cursors);
};

/** A `{name}` parameter occupying a whole segment, matching the server's `ProxyRoutePath` grammar. */
const ROUTE_PARAM = /^\{[A-Za-z_][A-Za-z0-9_]*\}$/;

export const trimRoutePath = (value: string | null | undefined) =>
  (value ?? "").trim().replace(/^\/+|\/+$/g, "");

/**
 * Validates a route template and returns its parameter names, or a human reason. `""` is the base
 * path and is valid. A `.` / `..` segment is refused: the server collapses them after building the
 * URL, so they would land the call on an endpoint no route declared.
 *
 * The console must reject exactly what the API rejects — otherwise a save fails with a field error
 * the form has no input to point at.
 */
export const parseRouteTemplate = (
  template: string,
): { ok: true; params: string[] } | { ok: false; reason: string } => {
  const trimmed = trimRoutePath(template);
  if (trimmed.length === 0) return { ok: true, params: [] };
  if (trimmed.length > 512) return { ok: false, reason: "Path must be 512 characters or fewer." };

  const segments = trimmed.split("/");
  if (segments.length > 20) return { ok: false, reason: "Path must have 20 segments or fewer." };

  const params: string[] = [];
  for (const segment of segments) {
    if (segment.length === 0) return { ok: false, reason: "Path must not contain an empty segment." };
    if (segment === "." || segment === "..")
      return { ok: false, reason: "Path must not contain a '.' or '..' segment." };
    if (!segment.includes("{") && !segment.includes("}")) continue;
    if (!ROUTE_PARAM.test(segment))
      return {
        ok: false,
        reason: `'${segment}' is not a valid parameter. Use {name} as a whole segment.`,
      };
    const name = segment.slice(1, -1);
    if (params.includes(name)) return { ok: false, reason: `Parameter '${name}' is declared twice.` };
    params.push(name);
  }

  return { ok: true, params };
};

/** The address a route is unique by. Two routes sharing it would make matching order-dependent. */
export const routeAddress = (method: string, path: string) => `${method} ${trimRoutePath(path)}`;

const routeSchema = z.object({
  method: z.enum(["GET", "POST", "PUT", "PATCH", "DELETE"]),
  path: z.string(),
  upstreamPath: z.string().nullable().default(null),
  headers: keyValueSchema.nullable().default(null),
  query: keyValueSchema.nullable().default(null),
  bodyMerge: keyValueSchema.nullable().default(null),
  responseMode: z.enum(["all", "select"]).nullable().default(null),
  responseInclude: z.array(z.string()).nullable().default(null),
});

export const proxyFormSchema = z
  .object({
    name: z.string().trim().min(1, "Give the proxy a name - it becomes the path."),
    upstreamUrl: z
      .string()
      .trim()
      .url("Please enter a valid url")
      .refine((url) => url.startsWith("https://"), "Use an https endpoint."),
    methods: z
      .array(z.enum(["GET", "POST", "PUT", "PATCH", "DELETE"]))
      .min(1, "Select at least one method."),
    headers: keyValueSchema,
    query: keyValueSchema,
    bodyMerge: keyValueSchema,
    bodyMode: z.enum(["passthrough", "merge"]),
    methodConfigs: z.array(methodOverrideSchema),
    routes: z.array(routeSchema).default([]),
    responseMode: z.enum(["all", "select"]).default("all"),
    responseInclude: z.array(z.string()).default([]),
  })
  .superRefine((values, ctx) => {
    const seenRoutes = new Set<string>();
    values.routes?.forEach((route, index) => {
      const parsed = parseRouteTemplate(route.path);
      if (!parsed.ok) {
        ctx.addIssue({ code: z.ZodIssueCode.custom, path: ["routes", index, "path"], message: parsed.reason });
        return;
      }

      // A route for a method the proxy does not allow can never be reached, and the API rejects it.
      if (!values.methods.includes(route.method)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["routes", index, "method"],
          message: `This proxy does not allow ${route.method}.`,
        });
      }

      const address = routeAddress(route.method, route.path);
      if (seenRoutes.has(address)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["routes", index, "path"],
          message: "This endpoint is already defined.",
        });
      }
      seenRoutes.add(address);

      if (route.upstreamPath == null || trimRoutePath(route.upstreamPath).length === 0) return;

      const upstream = parseRouteTemplate(route.upstreamPath);
      if (!upstream.ok) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["routes", index, "upstreamPath"],
          message: upstream.reason,
        });
        return;
      }

      // Rewriting to a parameter the client path does not declare would send a literal "{name}"
      // segment to the vendor.
      const undeclared = upstream.params.find((param) => !parsed.params.includes(param));
      if (undeclared) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["routes", index, "upstreamPath"],
          message: `{${undeclared}} is not a parameter of the client path.`,
        });
      }
    });

    if (values.responseMode === "select") {
      if (values.responseInclude.length > MAX_RESPONSE_PATHS) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["responseInclude"],
          message: `Keep at most ${MAX_RESPONSE_PATHS} response fields.`,
        });
      }
      values.responseInclude.forEach((path, index) => {
        if (!isResponsePath(path)) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["responseInclude", index],
            message: `'${path}' is not a valid field path.`,
          });
        }
      });
    }

    if (values.bodyMode === "merge") {
      const bodyMethods = values.methods.some(
        (method) => method === "POST" || method === "PUT" || method === "PATCH",
      );
      if (!bodyMethods) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["bodyMode"],
          message: "Body fields apply to POST, PUT or PATCH only.",
        });
      }

      const seen = new Set<string>();
      values.bodyMerge.forEach((row, index) => {
        const key = row.key.trim();
        if (!key) return;
        if (seen.has(key)) {
          ctx.addIssue({
            code: z.ZodIssueCode.custom,
            path: ["bodyMerge", index, "key"],
            message: "Each body field key must be unique.",
          });
        }
        seen.add(key);
      });
    }

    values.methodConfigs.forEach((override, index) => {
      if (!values.methods.includes(override.method)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["methodConfigs", index, "method"],
          message: "Enable this method above before configuring it.",
        });
      }

      const upstream = override.upstream?.trim();
      if (upstream && !/^https:\/\/.+/i.test(upstream)) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["methodConfigs", index, "upstream"],
          message: "Please enter a valid url",
        });
      }
    });
  });

export const proxyFormDefaultValues: ProxyFormValues = {
  name: "",
  upstreamUrl: "",
  methods: ["GET"],
  headers: [],
  query: [],
  bodyMerge: [],
  bodyMode: "passthrough",
  methodConfigs: [],
  routes: [],
  responseMode: "all",
  responseInclude: [],
};

export const compactKeyValues = (rows: ProxyFormValues["headers"]) =>
  rows
    .map((row) => ({ ...row, key: row.key.trim(), value: row.value.trim() }))
    .filter((row) => row.key || row.value);

/**
 * Shell-quote a value for a POSIX `curl` line. Single quotes are the only safe wrapper for
 * arbitrary URLs and header values; an embedded `'` is closed, escaped, and reopened.
 */
const shellQuote = (value: string) => `'${value.replace(/'/g, String.raw`'\''`)}'`;

/**
 * Rebuild a request log row as a runnable `curl` for the **client-facing gateway call** — what the
 * tenant's own client sent, not what Blocks forwarded upstream.
 *
 * Two deliberate omissions, both of which would make the command wrong rather than merely
 * incomplete:
 *
 * - **Injected headers / query params are not included.** Blocks attaches those server-side from the
 *   proxy configuration; a client that sent them itself would be duplicating credentials it is not
 *   supposed to hold. `injectedHeaderKeys` are also keys only — the values are never stored.
 * - **The real credentials are placeholders.** Execution rows store no request headers or body, so
 *   the tenant key and bearer cannot be recovered from a log; they have to be filled in by hand.
 */
export const buildProxyCurl = (
  log: Pick<ProxyExecutionLog, "method" | "path" | "requestQuery">,
  origin: string,
) => {
  const query = log.requestQuery ? `?${log.requestQuery}` : "";
  const url = `${origin.replace(/\/+$/, "")}${log.path}${query}`;

  const lines = [
    `curl -X ${log.method} ${shellQuote(url)}`,
    `  -H ${shellQuote("x-blocks-key: <your tenant id>")}`,
    `  -H ${shellQuote("Authorization: Bearer <token issued for that tenant>")}`,
  ];

  // Trailing backslash + real newline: a multi-line command the shell reads as one line.
  return lines.join(" \\\n");
};

/** `{name}` segments of a route template, in order and de-duplicated. */
export const routeParamNames = (path: string): string[] => [
  ...new Set([...path.matchAll(/\{([^{}/]+)\}/g)].map((match) => match[1])),
];

/**
 * Substitute a route template's `{name}` segments with caller-supplied values. A segment with no
 * value is left as-is, so the caller can show the unresolved template and refuse to send.
 */
export const fillRouteParams = (path: string, values: Record<string, string>): string =>
  path.replace(/\{([^{}/]+)\}/g, (token, name: string) => {
    const value = values[name]?.trim();
    return value ? encodeURIComponent(value) : token;
  });

/** Pretty-print a JSON body; return the text unchanged when it is not JSON. */
export const formatProxyBody = (body: string, contentType?: string) => {
  const trimmed = body.trim();
  const looksJson =
    (contentType?.toLowerCase().includes("json") ?? false) ||
    trimmed.startsWith("{") ||
    trimmed.startsWith("[");
  if (!looksJson) return body;
  try {
    return JSON.stringify(JSON.parse(trimmed), null, 2);
  } catch {
    return body;
  }
};
