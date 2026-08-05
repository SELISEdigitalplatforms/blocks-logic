"use client";

import Editor, {
  useMonaco,
  type Monaco,
  type OnMount,
} from "@monaco-editor/react";
import { useEffect, useRef } from "react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { FieldProps } from "../form-field.types";
import { cn } from "@/lib/utils";

// Per @monaco-editor/react docs, Monaco is loaded from CDN by default —
// no bundling, no worker setup, no direct `monaco-editor` import needed.
// Importing `monaco-editor` directly in a Vite project forces Vite to
// transform its entire ESM tree and exhausts the JS heap (OOM).

const SCRIPT_GLOBALS = `
declare const $items: Array<{ json: Record<string, unknown> }>;
declare const $json: { json: Record<string, unknown> };
declare const $node: Record<string, unknown>;
`;

const configureMonaco = (monaco: Monaco) => {
  monaco.typescript.javascriptDefaults.setCompilerOptions({
    target: monaco.typescript.ScriptTarget.ESNext,
    allowNonTsExtensions: true,
    moduleResolution: monaco.typescript.ModuleResolutionKind.NodeJs,
    module: monaco.typescript.ModuleKind.ESNext,
    noEmit: true,
    esModuleInterop: true,
    allowJs: true,
    lib: ["esnext"],
  });
  monaco.typescript.javascriptDefaults.setEagerModelSync(true);
  monaco.typescript.javascriptDefaults.addExtraLib(
    SCRIPT_GLOBALS,
    "ts:filename/script-globals.d.ts",
  );
  monaco.typescript.javascriptDefaults.setDiagnosticsOptions({
    noSemanticValidation: false,
    noSyntaxValidation: false,
    diagnosticCodesToIgnore: [2304, 7016],
  });
};

export const getCodeEditorSuggestionValues = (keys: string[]) => [
  "$json",
  "$json.json",
  ...keys.map((key) => `$json.json.${key}`),
  "$items",
];

const getMonacoLanguage = (value: string | undefined): string => {
  if (value === "json" || value === "javascript" || value === "html" || value === "css") {
    return value;
  }
  return "javascript";
};

export const CodeEditorFieldV2 = ({
  field,
  value,
  onChange,
  readOnly,
  config,
  className,
  placeholder,
}: FieldProps<string>) => {
  const { resolvedTheme } = useTheme();
  const language = getMonacoLanguage(field.language);
  const theme = resolvedTheme === "dark" ? "vs-dark" : "vs";
  const isDisabled =
    typeof field.disabled === "function" ? readOnly : field.disabled || readOnly;

  const upstreamKeys =
    (config as { upstreamKeys?: string[] } | undefined)?.upstreamKeys ?? [];
  const monaco = useMonaco();
  const providerRef = useRef<{ dispose: () => void } | null>(null);

  // Register `$...` completion provider once Monaco is loaded.
  useEffect(() => {
    if (!monaco) return;
    providerRef.current = monaco.languages.registerCompletionItemProvider(language, {
      triggerCharacters: ["$", "."],
      provideCompletionItems: (model, position) => {
        const line = model
          .getLineContent(position.lineNumber)
          .slice(0, position.column - 1);
        const match = line.match(/\$[\w.]*$/);
        if (!match) return { suggestions: [] };
        const startColumn = position.column - match[0].length;
        const range = {
          startLineNumber: position.lineNumber,
          endLineNumber: position.lineNumber,
          startColumn,
          endColumn: position.column,
        };
        return {
          suggestions: getCodeEditorSuggestionValues(upstreamKeys).map((suggestion) => ({
            label: suggestion,
            kind: monaco.languages.CompletionItemKind.Variable,
            insertText: suggestion,
            filterText: suggestion,
            range,
          })),
        };
      },
    });
    return () => {
      providerRef.current?.dispose();
      providerRef.current = null;
    };
  }, [monaco, language, upstreamKeys]);

  const handleMount: OnMount = (editor) => {
    // The inspector renders inside a Radix Sheet whose FocusScope traps
    // keyboard events. Monaco's offscreen textarea is treated as focusable,
    // so Radix intercepts keydown (notably Space) before Monaco can type it.
    // Stopping propagation in the bubble phase (after Monaco's own listeners
    // on the textarea have already handled the key) keeps the event from
    // reaching Radix without blocking Monaco itself.
    const container = editor.getContainerDomNode();
    const stopKeyPropagation = (event: KeyboardEvent) => {
      event.stopPropagation();
    };
    const reassertFocus = () => editor.focus();
    const textarea = container.querySelector<HTMLTextAreaElement>("textarea.inputarea");
    if (textarea) textarea.tabIndex = -1;
    container.addEventListener("keydown", stopKeyPropagation);
    container.addEventListener("keyup", stopKeyPropagation);
    container.addEventListener("keypress", stopKeyPropagation);
    container.addEventListener("pointerdown", reassertFocus);
    editor.onDidDispose(() => {
      container.removeEventListener("keydown", stopKeyPropagation);
      container.removeEventListener("keyup", stopKeyPropagation);
      container.removeEventListener("keypress", stopKeyPropagation);
      container.removeEventListener("pointerdown", reassertFocus);
    });
  };

  const placeholderText = placeholder || field.placeholder;

  return (
    <div className={cn("relative flex-1", className)}>
      <Editor
        height="300px"
        language={language}
        theme={theme}
        value={value ?? ""}
        onChange={(next) => onChange?.(next ?? "")}
        beforeMount={configureMonaco}
        onMount={handleMount}
        options={{
          readOnly: Boolean(isDisabled),
          minimap: { enabled: false },
          fontSize: 13,
          scrollBeyondLastLine: false,
          automaticLayout: true,
          fixedOverflowWidgets: true,
        }}
        className="overflow-hidden rounded-md border"
      />
      {placeholderText && !(value ?? "") && (
        <div
          aria-hidden
          className="pointer-events-none absolute select-none whitespace-pre-wrap font-mono text-sm text-muted-foreground"
          style={{ top: 8, left: 8, right: 8 }}
        >
          {placeholderText}
        </div>
      )}
    </div>
  );
};
