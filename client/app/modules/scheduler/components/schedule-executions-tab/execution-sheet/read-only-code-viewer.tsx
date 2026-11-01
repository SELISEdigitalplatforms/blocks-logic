import { useMemo } from "react";
import Editor from "@monaco-editor/react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { CopyIconButton } from "../copy-icon-button";

interface ReadOnlyCodeViewerProps {
  value: string;
  /** What the copy button copies, e.g. "Response body". */
  label: string;
  /** Forces a language; by default JSON when the text parses as JSON, otherwise plain text. */
  language?: "json" | "plaintext";
}

const VIEWER_OPTIONS = {
  readOnly: true,
  minimap: { enabled: false },
  fontSize: 13,
  scrollBeyondLastLine: false,
  automaticLayout: true,
  tabSize: 2,
} as const;

export function detectLanguage(value: string): "json" | "plaintext" {
  try {
    JSON.parse(value);
    return "json";
  } catch {
    // Not JSON: show it as text, never rendered.
    return "plaintext";
  }
}

/** Indents JSON two spaces; anything else is returned untouched. */
export function beautify(value: string, language: "json" | "plaintext"): string {
  if (language !== "json") return value;
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

/** Read-only Monaco (text only, never HTML) with the app theme and a copy button shown on hover. */
export const ReadOnlyCodeViewer = ({ value, label, language }: Readonly<ReadOnlyCodeViewerProps>) => {
  const { resolvedTheme } = useTheme();
  const lang = language ?? detectLanguage(value);
  const display = useMemo(() => beautify(value, lang), [value, lang]);

  return (
    <div className="group relative">
      <Editor
        height="200px"
        language={lang}
        theme={resolvedTheme === "dark" ? "vs-dark" : "vs"}
        value={display}
        options={VIEWER_OPTIONS}
        className="overflow-hidden rounded-lg border"
      />
      <CopyIconButton
        value={display}
        label={label}
        className="absolute right-4 top-2 z-10 bg-background/80 opacity-0 transition-opacity focus-visible:opacity-100 group-focus-within:opacity-100 group-hover:opacity-100"
      />
    </div>
  );
};
