/** One field to split out, as saved in the node's parameters (`fields`). */
export type SplitOutField = { fieldName: string; outputFieldName: string };

export const emptySplitOutField = (): SplitOutField => ({ fieldName: "", outputFieldName: "" });

/**
 * A field as a plain path. A field dragged from the input panel arrives as {{$json.output.lines}}; the
 * server reads the same forms, so this only tidies what the box shows.
 */
export const toFieldPath = (text: string): string => {
  let path = text.trim();
  const expression = /^\{\{\s*([\s\S]*?)\s*\}\}$/.exec(path);
  if (expression) path = expression[1];
  if (path.startsWith("$json.output.")) return path.slice("$json.output.".length);
  if (path.startsWith("$json.")) return path.slice("$json.".length);
  return path;
};

/** The saved fields, tolerant of anything malformed. */
export const splitOutFields = (value: unknown): SplitOutField[] =>
  Array.isArray(value)
    ? value
        .filter((entry): entry is Record<string, unknown> => typeof entry === "object" && entry !== null)
        .map((entry) => ({
          fieldName: typeof entry.fieldName === "string" ? entry.fieldName : "",
          outputFieldName: typeof entry.outputFieldName === "string" ? entry.outputFieldName : "",
        }))
    : [];

/** "email, name" as a list, and back: Fields To Include is typed as text but saved as a list. */
export const textToList = (text: string): string[] => text.split(",").map((part) => part.trim()).filter(Boolean);
export const listToText = (value: unknown): string =>
  Array.isArray(value) ? value.filter((part) => typeof part === "string").join(", ") : typeof value === "string" ? value : "";

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

const hasPath = (item: unknown, path: string, dotNotation: boolean): boolean => {
  if (!isRecord(item)) return false;
  if (!dotNotation) return Object.prototype.hasOwnProperty.call(item, path);
  let current: unknown = item;
  for (const segment of path.split(".")) {
    if (!isRecord(current) || !Object.prototype.hasOwnProperty.call(current, segment)) return false;
    current = current[segment];
  }
  return true;
};

/** The fields to split out that no input item has (n8n's "wasn't found in any input item" hint). */
export const fieldsNotFound = (fields: unknown, inputRows: unknown[], dotNotation: boolean): string[] => {
  if (inputRows.length === 0) return [];
  return splitOutFields(fields)
    .map((entry) => toFieldPath(entry.fieldName))
    .filter(Boolean)
    .filter((field) => !inputRows.some((row) => hasPath(row, field, dotNotation)));
};
