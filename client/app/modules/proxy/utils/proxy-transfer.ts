import { PROXY_IMPORT_MAX_FILE_BYTES } from "../constants";
import { ProxyExportFile } from "../types";

/** The file name an export is saved under: `proxy-{slug}.json`, the same name the server suggests. */
export const exportFileName = (slug: string) => `proxy-${slug || "proxy"}.json`;

/**
 * Saves an export file in the browser. Pretty-printed for people; the server checks the content hash
 * over the parsed config, so formatting never breaks an import.
 */
export const downloadExportFile = (fileName: string, file: ProxyExportFile): void => {
  const blob = new Blob([JSON.stringify(file, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
};

/** The schema the server reads (ProxyTransferCodec.SchemaVersion). */
export const PROXY_EXPORT_SCHEMA_VERSION = 1;

/**
 * What can be told from the name and size alone, so a wrong file is refused before it is read.
 * Returns the message to show, or null when the file may go on.
 */
export const checkImportFile = (file: File): string | null => {
  if (!file.name.toLowerCase().endsWith(".json")) return "Only .json export files can be imported.";
  if (file.size === 0) return "This file is empty.";
  if (file.size > PROXY_IMPORT_MAX_FILE_BYTES) {
    return "This file is larger than 1 MB, so it cannot be a proxy export.";
  }
  return null;
};

/**
 * Reads a picked file as a proxy export. Only shape is checked here (type, size, JSON, the format
 * name and schema); every real check — hash, id, config, secrets — is the server's, on preview and
 * again on apply.
 */
export const readImportFile = async (
  file: File,
): Promise<{ ok: true; file: ProxyExportFile } | { ok: false; error: string }> => {
  const refusal = checkImportFile(file);
  if (refusal) return { ok: false, error: refusal };

  let parsed: unknown;
  try {
    parsed = JSON.parse(await file.text());
  } catch {
    return { ok: false, error: "This file is not valid JSON." };
  }

  const head =
    parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as ProxyExportFile)
      : null;
  if (head?.format === "blocks.function.export") {
    return { ok: false, error: "This is a function export, not a proxy export." };
  }
  if (head?.format !== "blocks.proxy.export") {
    return { ok: false, error: "This is not a Blocks proxy export file." };
  }
  if (head.schemaVersion !== PROXY_EXPORT_SCHEMA_VERSION) {
    return {
      ok: false,
      error: `This export uses schema version ${String(head.schemaVersion ?? "(none)")}, but this environment reads version ${PROXY_EXPORT_SCHEMA_VERSION}. Export the proxy again and import the new file.`,
    };
  }

  return { ok: true, file: head };
};
