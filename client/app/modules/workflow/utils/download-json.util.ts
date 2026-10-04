/**
 * Trigger a browser download of `data` serialised as pretty-printed JSON.
 *
 * Kept deliberately tiny and side-effect-only so the export/import orchestration
 * can be unit tested without touching the DOM.
 */
export const downloadJson = (filename: string, data: unknown): void => {
  const json = JSON.stringify(data, null, 2);
  const blob = new Blob([json], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
};
