import Editor, { type OnMount } from "@monaco-editor/react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";

export type CodeEditorLanguage = "javascript" | "json";

type CodeEditorProps = {
  language: CodeEditorLanguage;
  value: string;
  onChange?: (value: string) => void;
  readOnly?: boolean;
  height?: string | number;
  className?: string;
};

/**
 * Thin `@monaco-editor/react` wrapper shared by the function editor's Code and Configuration
 * tabs. Stops keyboard events reaching the page shell (Ctrl+S, arrow keys) while the editor is
 * focused, matching the schedule module's payload editor.
 */
export const CodeEditor = ({
  language,
  value,
  onChange,
  readOnly = false,
  height = "420px",
  className,
}: CodeEditorProps) => {
  const { resolvedTheme } = useTheme();
  const monacoTheme = resolvedTheme === "dark" ? "vs-dark" : "vs";

  const handleMount: OnMount = (editor) => {
    const container = editor.getContainerDomNode();
    const stopKeyPropagation = (event: KeyboardEvent) => {
      const isSave = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "s";
      if (isSave) return;
      event.stopPropagation();
    };
    container.addEventListener("keydown", stopKeyPropagation);
    container.addEventListener("keyup", stopKeyPropagation);
    container.addEventListener("keypress", stopKeyPropagation);
    editor.onDidDispose(() => {
      container.removeEventListener("keydown", stopKeyPropagation);
      container.removeEventListener("keyup", stopKeyPropagation);
      container.removeEventListener("keypress", stopKeyPropagation);
    });
  };

  return (
    <Editor
      height={height}
      language={language}
      theme={monacoTheme}
      value={value}
      onChange={(next) => onChange?.(next ?? "")}
      onMount={handleMount}
      options={{
        readOnly,
        minimap: { enabled: false },
        fontSize: 13,
        scrollBeyondLastLine: false,
        automaticLayout: true,
        fixedOverflowWidgets: true,
        tabSize: 2,
      }}
      className={className ?? "overflow-hidden rounded-lg border"}
    />
  );
};
