/** Top-level JSON keys the user cannot change, with their string values. */
export type LockedKeys = Record<string, string>;

const inString = (n: number) => `__blocks_expr_${n}__`;
const bare = (n: number) => `__blocks_bare_${n}__`;
const PLACEHOLDER_IN_TEXT = /"__blocks_bare_(\d+)__"|__blocks_expr_(\d+)__/g;
const PLACEHOLDER_IN_VALUE = /__blocks_(?:bare|expr)_(\d+)__/g;

/**
 * Swaps every `{{…}}` expression for a placeholder JSON.parse accepts: plain text inside a string
 * literal, a quoted string where the expression stands as a bare value.
 */
export const shieldExpressions = (text: string) => {
  const tokens: string[] = [];
  let out = "";
  let quoted = false;
  let i = 0;
  while (i < text.length) {
    if (text.startsWith("{{", i)) {
      const end = text.indexOf("}}", i + 2);
      if (end !== -1) {
        const n = tokens.push(text.slice(i, end + 2)) - 1;
        out += quoted ? inString(n) : `"${bare(n)}"`;
        i = end + 2;
        continue;
      }
    }
    const ch = text[i];
    if (quoted && ch === "\\") {
      out += text.slice(i, i + 2);
      i += 2;
      continue;
    }
    if (ch === '"') quoted = !quoted;
    out += ch;
    i += 1;
  }
  return { shielded: out, tokens };
};

const parseObject = (text: string) => {
  const { shielded, tokens } = shieldExpressions(text);
  try {
    const parsed: unknown = JSON.parse(shielded);
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return { object: parsed as Record<string, unknown>, tokens };
    }
  } catch {
    // Not JSON (yet): the caller keeps the text as typed.
  }
  return null;
};

const restoreText = (text: string, tokens: string[]) =>
  text.replace(PLACEHOLDER_IN_TEXT, (match, a, b) => tokens[Number(a ?? b)] ?? match);

const restoreValue = (value: unknown, tokens: string[]) =>
  typeof value === "string"
    ? value.replace(PLACEHOLDER_IN_VALUE, (match, n) => tokens[Number(n)] ?? match)
    : value;

const pretty = (object: Record<string, unknown>, tokens: string[]) =>
  restoreText(JSON.stringify(object, null, 2), tokens);

/**
 * `text` with the locked keys in front, at their locked values. Text that is not a JSON object is
 * returned as is; empty text becomes the locked keys alone.
 */
export const withLockedKeys = (text: string, locked: LockedKeys): string => {
  if (!Object.keys(locked).length) return text;
  if (!text.trim()) return JSON.stringify(locked, null, 2);
  const parsed = parseObject(text);
  if (!parsed) return text;
  const rest = Object.fromEntries(
    Object.entries(parsed.object).filter(([key]) => !(key in locked)),
  );
  return pretty({ ...locked, ...rest }, parsed.tokens);
};

/**
 * `text` without the locked keys, which is what gets saved, and an error naming any locked key the
 * user changed or removed. Text that is not a JSON object is returned as typed.
 */
export const withoutLockedKeys = (
  text: string,
  locked: LockedKeys,
): { value: string; error?: string } => {
  const keys = Object.keys(locked);
  if (!keys.length) return { value: text };
  const parsed = parseObject(text);
  if (!parsed) return { value: text };

  const changed = keys.filter(
    (key) => !(key in parsed.object) || restoreValue(parsed.object[key], parsed.tokens) !== locked[key],
  );
  const rest = Object.fromEntries(
    Object.entries(parsed.object).filter(([key]) => !(key in locked)),
  );
  return {
    value: pretty(rest, parsed.tokens),
    error: changed.length
      ? `${changed.map((key) => `"${key}"`).join(", ")} ${changed.length === 1 ? "is" : "are"} locked and can't be changed or removed.`
      : undefined,
  };
};
