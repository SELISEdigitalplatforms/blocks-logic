import type { IFunctionExportFile } from "../types/transfer.types";
import { tStatic } from "@seliseblocks/genesis-os";

/** The server's ceiling for an import (FunctionTransferCodec.MaxImportBytes): checked here first. */
export const MAX_IMPORT_BYTES = 3 * 1024 * 1024;

export const EXPORT_FORMAT = "blocks.function.export";

/** The schema the server reads (FunctionTransferCodec.SchemaVersion). */
export const EXPORT_SCHEMA_VERSION = 2;

/**
 * What can be told from the name and size alone, so a wrong file is refused before it is read.
 * Returns the message to show, or null when the file may go on.
 */
export const checkImportFile = (file: File): string | null => {
  if (!file.name.toLowerCase().endsWith(".json"))
    return tStatic("logic.functions", "TRANSFER.ONLY_JSON_EXPORT_FILES");
  if (file.size === 0) return tStatic("logic.functions", "TRANSFER.THIS_FILE_IS_EMPTY");
  if (file.size > MAX_IMPORT_BYTES) {
    return tStatic("logic.functions", "TRANSFER.THIS_FILE_IS_LARGER");
  }
  return null;
};

/**
 * Reads a chosen file as an export. Refuses what is clearly not one — wrong type, too big, not
 * JSON, not this format or schema — before anything is sent. The server checks everything again
 * (hash, secrets), so this only saves a round trip and gives a plain reason.
 */
export const readImportFile = async (file: File): Promise<IFunctionExportFile> => {
  const refusal = checkImportFile(file);
  if (refusal) throw new Error(refusal);
  let parsed: unknown;
  try {
    parsed = JSON.parse(await file.text());
  } catch {
    throw new Error("This file is not valid JSON.");
  }
  const head =
    parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as { format?: unknown; schemaVersion?: unknown })
      : null;
  if (head?.format === "blocks.proxy.export") {
    throw new Error("This is a proxy export, not a function export.");
  }
  if (head?.format !== EXPORT_FORMAT) throw new Error("This is not a function export file.");
  if (head.schemaVersion !== EXPORT_SCHEMA_VERSION) {
    throw new Error(
      `This export uses schema version ${String(head.schemaVersion ?? "(none)")}, but this environment reads version ${EXPORT_SCHEMA_VERSION}. Export the function again and import the new file.`,
    );
  }
  return parsed as IFunctionExportFile;
};

/** `function-{name}-{id8}.json`, the same name the server offers. */
export const exportFileName = (file: IFunctionExportFile) => {
  const name =
    (file.function?.name ?? "function")
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 40)
      .replace(/-+$/g, "") || "function";
  return `function-${name}-${(file.function?.itemId ?? "").slice(0, 8)}.json`;
};

/** Saves the export as a pretty-printed JSON file through a temporary link. */
export const downloadExportFile = (file: IFunctionExportFile) => {
  const blob = new Blob([JSON.stringify(file, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = exportFileName(file);
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
};

/**
 * The status and the readable message of a failed import call. The http client throws an error
 * carrying `status` and the response body (`{ isSuccess, errors: { key: message } }`).
 */
export const importErrorOf = (error: unknown): { status?: number; message: string } => {
  const raw = error as { status?: unknown; errors?: unknown; message?: unknown } | null;
  const status = typeof raw?.status === "number" ? raw.status : undefined;
  const body = raw?.errors;
  const inner =
    body && typeof body === "object" && "errors" in (body as object)
      ? (body as { errors?: unknown }).errors
      : body;
  let message: string | undefined;
  if (typeof inner === "string") message = inner;
  else if (inner && typeof inner === "object") {
    const values = Object.values(inner as Record<string, unknown>).filter(
      (v): v is string => typeof v === "string" && v.length > 0,
    );
    if (values.length) message = values.join(" ");
  }
  if (!message && typeof raw?.message === "string" && raw.message) message = raw.message;
  return {
    status,
    message: message ?? tStatic("logic.functions", "TRANSFER.THE_REQUEST_COULD_NOT"),
  };
};

// ─── secrets kept by name ────────────────────────────────────────────────────

/**
 * `{{secretName.<name>}}` — an imported reference not bound to an id yet. Same pattern as the
 * server's `FunctionSecretRefs.NamePattern` (a Blocks OS secret name). Fresh regex per call.
 */
export const secretNamePattern = () => /\{\{secretName\.([A-Za-z0-9][A-Za-z0-9._-]{0,99})\}\}/g;

/** The export marker for a reference that had no secret where the file was made. */
export const UNRESOLVED_SECRET_MARKER = "{{secretUnresolved}}";

/** The name when `value` is exactly one `{{secretName.X}}`, otherwise null. */
export const soleSecretName = (value: string | null | undefined): string | null => {
  const match = (value ?? "")
    .trim()
    .match(/^\{\{secretName\.([A-Za-z0-9][A-Za-z0-9._-]{0,99})\}\}$/);
  return match ? match[1] : null;
};

/** Every name referenced in `value`, in order. */
export const secretNamesIn = (value: string | null | undefined): string[] =>
  [...(value ?? "").matchAll(secretNamePattern())].map((m) => m[1]);
