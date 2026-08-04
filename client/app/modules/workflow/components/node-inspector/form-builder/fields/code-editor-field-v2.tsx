"use client";

"use client";

import { useEffect, useRef } from "react";
import * as monaco from "monaco-editor";
// Worker imports use absolute paths because monaco-editor's `exports` map
// doesn't expose subpaths with the `?worker` query. The `?worker` suffix is a
// Vite convention that bundles each file as a Web Worker.
import editorWorker from "/node_modules/monaco-editor/esm/vs/editor/editor.worker.js?worker";
import jsonWorker from "/node_modules/monaco-editor/esm/vs/language/json/json.worker.js?worker";
import cssWorker from "/node_modules/monaco-editor/esm/vs/language/css/css.worker.js?worker";
import htmlWorker from "/node_modules/monaco-editor/esm/vs/language/html/html.worker.js?worker";
import tsWorker from "/node_modules/monaco-editor/esm/vs/language/typescript/ts.worker.js?worker";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { FieldProps } from "../form-field.types";
import { cn } from "@/lib/utils";

self.MonacoEnvironment = {
  getWorker(_workerId, label) {
    switch (label) {
      case "json":
        return new jsonWorker();
      case "css":
      case "scss":
      case "less":
        return new cssWorker();
      case "html":
      case "handlebars":
      case "razor":
        return new htmlWorker();
      case "typescript":
      case "javascript":
        return new tsWorker();
      default:
        return new editorWorker();
    }
  },
};

const SCRIPT_GLOBALS = `
declare const $items: Array<{ json: Record<string, unknown> }>;
declare const $json: { json: Record<string, unknown> };
declare const $node: Record<string, unknown>;
`;

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
monaco.typescript.javascriptDefaults.addExtraLib(SCRIPT_GLOBALS, "ts:filename/script-globals.d.ts");
monaco.typescript.javascriptDefaults.setDiagnosticsOptions({
  noSemanticValidation: false,
  noSyntaxValidation: false,
  diagnosticCodesToIgnore: [2304, 7016],
});

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
  const containerRef = useRef<HTMLDivElement>(null);
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const providerRef = useRef<monaco.IDisposable | null>(null);
  const upstreamKeysRef = useRef<string[]>([]);
  const onChangeRef = useRef(onChange);
  const { resolvedTheme } = useTheme();
  const language = getMonacoLanguage(field.language);
  const theme = resolvedTheme === "dark" ? "vs-dark" : "vs";
  const isDisabled = typeof field.disabled === "function" ? readOnly : field.disabled || readOnly;

  useEffect(() => {
    upstreamKeysRef.current = [];
  }, []);

  useEffect(() => {
    onChangeRef.current = onChange;
  });

  useEffect(() => {
    if (!containerRef.current) return;
    const instance = monaco.editor.create(containerRef.current, {
      value: value ?? "",
      language,
      theme,
      readOnly: Boolean(isDisabled),
      minimap: { enabled: false },
      fontSize: 13,
      scrollBeyondLastLine: false,
      automaticLayout: true,
      fixedOverflowWidgets: true,
    });
    editorRef.current = instance;

    const ensureTextarea = () => {
      const textarea =
        containerRef.current?.querySelector<HTMLTextAreaElement>("textarea.inputarea");
      if (!textarea) return;
      textarea.tabIndex = -1;
    };
    ensureTextarea();
    const reassertFocus = () => instance.focus();
    const container = containerRef.current;

    // The inspector renders inside a Radix Sheet whose FocusScope traps
    // keyboard events. Monaco's offscreen textarea is treated as focusable,
    // so Radix intercepts keydown (notably Space) before Monaco can type it.
    // Stopping propagation in the bubble phase (after Monaco's own listeners
    // on the textarea have already handled the key) keeps the event from
    // reaching Radix without blocking Monaco itself.
    const stopKeyPropagation = (event: KeyboardEvent) => {
      event.stopPropagation();
    };
    container?.addEventListener("keydown", stopKeyPropagation);
    container?.addEventListener("keyup", stopKeyPropagation);
    container?.addEventListener("keypress", stopKeyPropagation);
    container?.addEventListener("pointerdown", reassertFocus);

    providerRef.current = monaco.languages.registerCompletionItemProvider(language, {
      triggerCharacters: ["$", "."],
      provideCompletionItems: (model, position) => {
        const line = model.getLineContent(position.lineNumber).slice(0, position.column - 1);
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
          suggestions: getCodeEditorSuggestionValues(upstreamKeysRef.current).map((suggestion) => ({
            label: suggestion,
            kind: monaco.languages.CompletionItemKind.Variable,
            insertText: suggestion,
            filterText: suggestion,
            range,
          })),
        };
      },
    });

    const subscription = instance.onDidChangeModelContent(() => {
      onChangeRef.current(instance.getValue());
    });

    return () => {
      subscription.dispose();
      providerRef.current?.dispose();
      providerRef.current = null;
      container?.removeEventListener("keydown", stopKeyPropagation);
      container?.removeEventListener("keyup", stopKeyPropagation);
      container?.removeEventListener("keypress", stopKeyPropagation);
      container?.removeEventListener("pointerdown", reassertFocus);
      instance.dispose();
      editorRef.current = null;
    };
  }, [language]);

  useEffect(() => {
    const instance = editorRef.current;
    if (!instance) return;
    if (instance.getValue() !== (value ?? "")) {
      instance.setValue(value ?? "");
    }
  }, [value]);

  useEffect(() => {
    monaco.editor.setTheme(theme);
  }, [theme]);

  useEffect(() => {
    editorRef.current?.updateOptions({ readOnly: Boolean(isDisabled) });
  }, [isDisabled]);

  const placeholderText = placeholder || field.placeholder;

  return (
    <div className={cn("relative flex-1", className)}>
      <div ref={containerRef} className="h-[300px] w-full overflow-hidden rounded-md border" />
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
