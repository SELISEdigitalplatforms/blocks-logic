import type { IFunctionExportFile } from "../types/transfer.types";

/** The server's ceiling for an import (FunctionTransferCodec.MaxImportBytes): checked here first. */
export const MAX_IMPORT_BYTES = 3 * 1024 * 1024;

export const EXPORT_FORMAT = "blocks.function.export";

/**
 * Reads a chosen file as an export. Refuses what is clearly not one — too big, not JSON, not the
 * format — before anything is sent. The server checks everything again (schema, hash, secrets).
 */
export const readImportFile = async (file: File): Promise<IFunctionExportFile> => {
  if (file.size > MAX_IMPORT_BYTES) {
    throw new Error("This file is larger than 3 MB, so it cannot be a function export.");
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(await file.text());
  } catch {
    throw new Error("This file is not valid JSON.");
  }
  if (
    !parsed ||
    typeof parsed !== "object" ||
    Array.isArray(parsed) ||
    (parsed as { format?: unknown }).format !== EXPORT_FORMAT
  ) {
    throw new Error("This is not a function export file.");
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
  return { status, message: message ?? "The request could not be completed." };
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
  const match = (value ?? "").trim().match(/^\{\{secretName\.([A-Za-z0-9][A-Za-z0-9._-]{0,99})\}\}$/);
  return match ? match[1] : null;
};

/** Every name referenced in `value`, in order. */
export const secretNamesIn = (value: string | null | undefined): string[] =>
  [...(value ?? "").matchAll(secretNamePattern())].map((m) => m[1]);
