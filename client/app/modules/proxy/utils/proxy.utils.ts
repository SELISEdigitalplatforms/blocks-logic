import { z } from "zod";
import { ProxyFormValues } from "../types";

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
  })
  .superRefine((values, ctx) => {
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
};

export const compactKeyValues = (rows: ProxyFormValues["headers"]) =>
  rows
    .map((row) => ({ ...row, key: row.key.trim(), value: row.value.trim() }))
    .filter((row) => row.key || row.value);
