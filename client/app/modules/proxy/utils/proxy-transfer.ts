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

/**
 * Reads a picked file as a proxy export. Only shape is checked here (size, JSON, the format name);
 * every real check — hash, id, config, secrets — is the server's, on preview and again on apply.
 */
export const readImportFile = async (
  file: File,
): Promise<{ ok: true; file: ProxyExportFile } | { ok: false; error: string }> => {
  if (file.size > PROXY_IMPORT_MAX_FILE_BYTES) {
    return { ok: false, error: "This file is too large to be a proxy export." };
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(await file.text());
  } catch {
    return { ok: false, error: "This file is not valid JSON." };
  }

  if (
    !parsed ||
    typeof parsed !== "object" ||
    (parsed as ProxyExportFile).format !== "blocks.proxy.export"
  ) {
    return { ok: false, error: "This is not a Blocks proxy export file." };
  }

  return { ok: true, file: parsed as ProxyExportFile };
};
