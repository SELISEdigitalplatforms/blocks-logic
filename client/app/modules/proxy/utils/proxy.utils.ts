import { z } from "zod";
import { ProxyFormValues } from "../types";

export const slugifyProxyName = (name: string) =>
  name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80);

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
      isSecretRef: z.boolean().optional(),
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

export const proxyFormSchema = z.object({
  name: z.string().trim().min(1, "Give the proxy a name - it becomes the path."),
  upstreamUrl: z
    .string()
    .trim()
    .url("Enter a valid https endpoint.")
    .refine((url) => url.startsWith("https://"), "Use an https endpoint."),
  methods: z.array(z.enum(["GET", "POST", "PUT", "PATCH", "DELETE"])).min(1, "Select at least one method."),
  headers: keyValueSchema,
  query: keyValueSchema,
});

export const proxyFormDefaultValues: ProxyFormValues = {
  name: "",
  upstreamUrl: "",
  methods: ["GET"],
  headers: [],
  query: [],
};

export const compactKeyValues = (rows: ProxyFormValues["headers"]) =>
  rows
    .map((row) => ({ ...row, key: row.key.trim(), value: row.value.trim() }))
    .filter((row) => row.key || row.value);
