import { ProxyKeyValue } from "../types";
import { keyCollisions } from "./effective-route";

const VAR_TOKEN_SPLIT_RE = /(\{\{\$VAR\.[A-Za-z0-9._:-]+\}\})/;
const VAR_TOKEN_EXACT_RE = /^\{\{\$VAR\.[A-Za-z0-9._:-]+\}\}$/;

/** URI-encode one query part, keeping `[` / `]` readable (`expand[]`) since servers accept them as-is. */
const encodeQueryPart = (value: string) =>
  encodeURIComponent(value).replace(/%5B/gi, "[").replace(/%5D/gi, "]");

/** Encode a value but leave any `{{$VAR.name}}` token verbatim so it stays readable. */
const encodeQueryValue = (value: string) =>
  value
    .split(VAR_TOKEN_SPLIT_RE)
    .map((part) => (VAR_TOKEN_EXACT_RE.test(part) ? part : encodeQueryPart(part)))
    .join("");

/** Configured query rows as a `k=v&…` string for the Test tab's query input. Empty keys are skipped. */
export const buildPrefillQuery = (rows: ProxyKeyValue[]): string =>
  rows
    .filter((row) => row.key.trim())
    .map((row) => `${encodeQueryPart(row.key.trim())}=${encodeQueryValue(row.value)}`)
    .join("&");

/**
 * Configured body fields as a pretty-printed JSON object for the Test tab's body input. No fields ⇒
 * `""`, so an endpoint that merges nothing starts with an empty body. A repeated key keeps its last
 * value, as the server's merge does.
 */
export const buildPrefillBody = (rows: ProxyKeyValue[]): string => {
  const fields = rows.filter((row) => row.key.trim());
  if (!fields.length) return "";
  return JSON.stringify(
    Object.fromEntries(fields.map((row) => [row.key.trim(), row.value])),
    null,
    2,
  );
};

const safeDecode = (value: string) => {
  try {
    return decodeURIComponent(value.replace(/\+/g, " "));
  } catch {
    return value;
  }
};

/** The keys of a typed query string (`?` optional), decoded, in order. */
export const queryStringKeys = (query: string): string[] =>
  query
    .trim()
    .replace(/^\?/, "")
    .split("&")
    .map((pair) => safeDecode(pair.split("=")[0] ?? "").trim())
    .filter(Boolean);

/** Top-level keys of a typed JSON body; `[]` when it is empty, not JSON, or not an object. */
export const jsonBodyKeys = (body: string): string[] => {
  if (!body.trim()) return [];
  try {
    const parsed: unknown = JSON.parse(body);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? Object.keys(parsed)
      : [];
  } catch {
    return [];
  }
};

/**
 * The typed keys whose value the proxy's configuration overrides, deduped in typed order. Both the
 * gateway's query build and its body merge match keys exactly, which is keyCollisions' "query" rule.
 */
export const overriddenKeys = (typedKeys: string[], configured: ProxyKeyValue[]): string[] => {
  const collisions = keyCollisions(
    typedKeys.map((key) => ({ key })),
    configured,
    "query",
  );
  return [...new Set(typedKeys.filter((_, i) => collisions[i].replacesConnection))];
};
