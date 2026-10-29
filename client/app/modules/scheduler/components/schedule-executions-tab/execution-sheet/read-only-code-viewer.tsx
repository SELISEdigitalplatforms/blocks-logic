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

/** Read-only Monaco (text only, never HTML) with the app theme and a copy button. */
export const ReadOnlyCodeViewer = ({ value, label, language }: Readonly<ReadOnlyCodeViewerProps>) => {
  const { resolvedTheme } = useTheme();

  return (
    <div className="space-y-1.5">
      <div className="flex justify-end">
        <CopyIconButton value={value} label={label} />
      </div>
      <Editor
        height="240px"
        language={language ?? detectLanguage(value)}
        theme={resolvedTheme === "dark" ? "vs-dark" : "vs"}
        value={value}
        options={VIEWER_OPTIONS}
        className="overflow-hidden rounded-lg border"
      />
    </div>
  );
};
