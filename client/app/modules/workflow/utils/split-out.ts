/**
 * Split Out editor helpers. The node still saves n8n's comma-separated strings (fieldToSplitOut,
 * destinationFieldName); the editor shows them as one row per field.
 */
export type SplitOutRow = { field: string; destination: string };

const parts = (text: unknown): string[] =>
  typeof text === "string" ? text.split(",").map((part) => part.trim()) : [];

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

export const rowsFromParameters = (fieldToSplitOut: unknown, destinationFieldName: unknown): SplitOutRow[] => {
  const fields = parts(fieldToSplitOut).filter(Boolean);
  const destinations = parts(destinationFieldName);
  const named = destinations.length === fields.length;
  const rows = fields.map((field, i) => ({ field, destination: named && destinations[i] !== field ? destinations[i] : "" }));
  return rows.length ? rows : [{ field: "", destination: "" }];
};

/**
 * The saved strings for the rows. Destination names are all-or-nothing on the server (one per field), so
 * when any row has one, the others use their own field name.
 */
export const parametersFromRows = (rows: SplitOutRow[]): { fieldToSplitOut: string; destinationFieldName: string } => {
  const filled = rows.map((row) => ({ field: row.field.trim(), destination: row.destination.trim() })).filter((row) => row.field);
  const anyDestination = filled.some((row) => row.destination);
  return {
    fieldToSplitOut: filled.map((row) => row.field).join(", "),
    destinationFieldName: anyDestination ? filled.map((row) => row.destination || row.field).join(", ") : "",
  };
};

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
export const fieldsNotFound = (fieldToSplitOut: unknown, inputRows: unknown[], dotNotation: boolean): string[] => {
  if (inputRows.length === 0) return [];
  return parts(fieldToSplitOut)
    .filter(Boolean)
    .map(toFieldPath)
    .filter((field) => !inputRows.some((row) => hasPath(row, field, dotNotation)));
};
