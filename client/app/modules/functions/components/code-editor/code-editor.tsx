import { useEffect, useMemo, useRef } from "react";
import Editor, { useMonaco, type Monaco, type OnMount } from "@monaco-editor/react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import {
  FUNCTION_TYPES_PATH,
  buildCtxCompletions,
  buildFunctionTypeDefs,
} from "../../utils/function-types";

// Per @monaco-editor/react docs, Monaco is loaded from CDN by default — no bundling, no worker
// setup, no direct `monaco-editor` import (importing it in a Vite project makes Vite transform
// its whole ESM tree and exhausts the JS heap).

export type CodeEditorLanguage = "javascript" | "json";

type CodeEditorProps = {
  language: CodeEditorLanguage;
  value: string;
  onChange?: (value: string) => void;
  readOnly?: boolean;
  height?: string | number;
  className?: string;
  /**
   * Variable keys bound to this function. They become `ctx.env.NAME` completions, so the editor
   * offers the tenant's own keys and not a generic string map.
   */
  envKeys?: string[];
};

/**
 * Monaco, configured to feel like VS Code: same default theme pair, sticky scroll, bracket-pair
 * colouring, indent guides, inline suggestions and hover.
 *
 * IntelliSense is JavaScript-only, on purpose. `index.js` gets the `FunctionContext` types plus
 * `ctx.env` keys; `package.json` keeps Monaco's own JSON support and none of this.
 */
const typescriptDefaults = (monaco: Monaco) =>
  // 0.56 exposes the TS language service as `monaco.typescript`; older builds only have the now
  // deprecated `monaco.languages.typescript`. Whichever the loaded CDN build has is the one used.
  monaco.typescript ??
  (monaco.languages as unknown as { typescript?: typeof monaco.typescript }).typescript;

/**
 * Monaco ships `vs` / `vs-dark`, which sit on their own greys and read as a foreign panel dropped
 * into the page. These two put the editor on the app's own surfaces (SELISE tokens) so the card,
 * the file tabs and the code all share one background, and keep the comment/keyword contrast up.
 */
const THEMES = {
  light: {
    name: "blocks-light",
    base: "vs" as const,
    colors: {
      "editor.background": "#FFFFFF",
      "editor.foreground": "#1B2021",
      "editorLineNumber.foreground": "#A9ACAF",
      "editorLineNumber.activeForeground": "#1B2021",
      "editor.lineHighlightBackground": "#F4F6F8",
      "editor.selectionBackground": "#CCE0EF",
      "editorIndentGuide.background1": "#E7EAEE",
      "editorIndentGuide.activeBackground1": "#C3C7CC",
      "editorGutter.background": "#FFFFFF",
      "editorWidget.background": "#FFFFFF",
      "editorWidget.border": "#E7EAEE",
      "editorSuggestWidget.background": "#FFFFFF",
      "scrollbarSlider.background": "#1B202120",
    },
    rules: [
      { token: "comment", foreground: "6A8759", fontStyle: "italic" },
      { token: "keyword", foreground: "0067A3" },
      { token: "string", foreground: "8E4B00" },
      { token: "number", foreground: "8E0021" },
    ],
  },
  dark: {
    name: "blocks-dark",
    base: "vs-dark" as const,
    colors: {
      "editor.background": "#14181A",
      "editor.foreground": "#E7EAEE",
      "editorLineNumber.foreground": "#5A6066",
      "editorLineNumber.activeForeground": "#E7EAEE",
      "editor.lineHighlightBackground": "#1D2225",
      "editorGutter.background": "#14181A",
      "editorWidget.background": "#1D2225",
      "editorWidget.border": "#2A3034",
      "editorSuggestWidget.background": "#1D2225",
    },
    rules: [],
  },
};

const defineThemes = (monaco: Monaco) => {
  (Object.values(THEMES) as (typeof THEMES)[keyof typeof THEMES][]).forEach((theme) => {
    monaco.editor.defineTheme(theme.name, {
      base: theme.base,
      inherit: true,
      rules: theme.rules,
      colors: theme.colors,
    });
  });
};

const configureJavaScript = (monaco: Monaco) => {
  const typescript = typescriptDefaults(monaco);
  if (!typescript) return;

  typescript.javascriptDefaults.setCompilerOptions({
    target: typescript.ScriptTarget.ESNext,
    module: typescript.ModuleKind.ESNext,
    moduleResolution: typescript.ModuleResolutionKind.NodeJs,
    allowNonTsExtensions: true,
    allowJs: true,
    // checkJs off, like VS Code for a plain .js file: completions and hover, no type errors on
    // ordinary untyped JavaScript.
    checkJs: false,
    esModuleInterop: true,
    noEmit: true,
    // The sandbox is Node 24 with a real fetch, URL, crypto and structuredClone.
    lib: ["esnext", "dom"],
  });

  typescript.javascriptDefaults.setDiagnosticsOptions({
    noSyntaxValidation: false,
    noSemanticValidation: false,
    // 2307 / 7016: an npm dependency declared in package.json cannot be resolved in the browser —
    // it is installed at deploy, so flagging every `import` from a pinned package is pure noise.
    diagnosticCodesToIgnore: [2307, 7016],
  });

  typescript.javascriptDefaults.setEagerModelSync(true);
};

export const CodeEditor = ({
  language,
  value,
  onChange,
  readOnly = false,
  height = "clamp(240px, 40vh, 460px)",
  className,
  envKeys,
}: CodeEditorProps) => {
  const { resolvedTheme } = useTheme();
  const monaco = useMonaco();
  const monacoTheme = resolvedTheme === "dark" ? THEMES.dark.name : THEMES.light.name;
  const isJavaScript = language === "javascript";

  // A stable key for the bound variables, so re-renders do not rebuild the type definitions.
  const envKey = useMemo(() => (envKeys ?? []).join(","), [envKeys]);

  const libRef = useRef<{ dispose: () => void } | null>(null);
  const completionRef = useRef<{ dispose: () => void } | null>(null);

  // Replace, never accumulate: `addExtraLib` returns a disposable, and re-adding the same path
  // without disposing leaves stale `ctx.env` keys behind after a variable is renamed. Disposing
  // ours (rather than `setExtraLibs`) leaves other modules' libraries alone.
  useEffect(() => {
    if (!monaco || !isJavaScript) return;
    const typescript = typescriptDefaults(monaco);
    if (!typescript) return;

    libRef.current?.dispose();
    libRef.current = typescript.javascriptDefaults.addExtraLib(
      buildFunctionTypeDefs(envKey ? envKey.split(",") : []),
      FUNCTION_TYPES_PATH,
    );

    return () => {
      libRef.current?.dispose();
      libRef.current = null;
    };
  }, [monaco, isJavaScript, envKey]);

  // `ctx.` completions that do not depend on the file carrying a JSDoc type annotation, so an
  // older function written before the typed starter still gets the platform's members.
  useEffect(() => {
    if (!monaco || !isJavaScript) return;

    completionRef.current?.dispose();
    completionRef.current = monaco.languages.registerCompletionItemProvider("javascript", {
      triggerCharacters: ["."],
      provideCompletionItems: (model, position) => {
        const line = model.getLineContent(position.lineNumber).slice(0, position.column - 1);
        const match = /\bctx\.((?:env\.)?[\w$]*)$/.exec(line);
        if (!match) return { suggestions: [] };

        const typed = match[1];
        const range = {
          startLineNumber: position.lineNumber,
          endLineNumber: position.lineNumber,
          startColumn: position.column - typed.length,
          endColumn: position.column,
        };

        return {
          suggestions: buildCtxCompletions(envKey ? envKey.split(",") : [])
            .filter((item) => (typed.startsWith("env.") ? item.label.startsWith("env.") : true))
            .map((item) => ({
              label: item.label,
              kind: monaco.languages.CompletionItemKind.Property,
              detail: item.detail,
              documentation: item.documentation,
              insertText: typed.startsWith("env.") ? item.label.slice("env.".length) : item.label,
              range,
            })),
        };
      },
    });

    return () => {
      completionRef.current?.dispose();
      completionRef.current = null;
    };
  }, [monaco, isJavaScript, envKey]);

  const handleMount: OnMount = (editor, monacoInstance) => {
    // Ctrl+S belongs to the page (it saves the function), so let that one through and keep every
    // other key inside the editor rather than the page shell's shortcuts.
    const container = editor.getContainerDomNode();
    const stopKeyPropagation = (event: KeyboardEvent) => {
      const isSave = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "s";
      if (isSave) return;
      event.stopPropagation();
    };
    container.addEventListener("keydown", stopKeyPropagation);
    container.addEventListener("keyup", stopKeyPropagation);
    container.addEventListener("keypress", stopKeyPropagation);

    // VS Code's own binding for "Format Document".
    editor.addCommand(
      monacoInstance.KeyMod.Shift | monacoInstance.KeyMod.Alt | monacoInstance.KeyCode.KeyF,
      () => void editor.getAction("editor.action.formatDocument")?.run(),
    );

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
      beforeMount={(monacoInstance) => {
        defineThemes(monacoInstance);
        configureJavaScript(monacoInstance);
      }}
      onMount={handleMount}
      options={{
        readOnly,
        // ── VS Code defaults ────────────────────────────────────────────────
        fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, "Courier New", monospace',
        fontSize: 13,
        fontLigatures: true,
        lineHeight: 20,
        // No minimap and no ruler: in a two-column editor panel they are the two things that
        // read as noise — a map of a 4-line file, and a vertical line down otherwise empty space.
        minimap: { enabled: false },
        stickyScroll: { enabled: false },
        bracketPairColorization: { enabled: true },
        guides: { bracketPairs: true, indentation: true, highlightActiveIndentation: true },
        renderLineHighlight: "all",
        renderWhitespace: "selection",
        matchBrackets: "always",
        occurrencesHighlight: "singleFile",
        selectionHighlight: true,
        cursorSmoothCaretAnimation: "on",
        smoothScrolling: true,
        linkedEditing: true,
        autoClosingBrackets: "languageDefined",
        autoClosingQuotes: "languageDefined",
        autoSurround: "languageDefined",
        formatOnPaste: true,
        formatOnType: true,
        // ── suggestions ─────────────────────────────────────────────────────
        quickSuggestions: { other: true, comments: false, strings: false },
        suggestOnTriggerCharacters: true,
        acceptSuggestionOnEnter: "on",
        tabCompletion: "on",
        wordBasedSuggestions: "currentDocument",
        parameterHints: { enabled: true },
        suggest: {
          showWords: true,
          showMethods: true,
          showFunctions: true,
          showProperties: true,
          preview: true,
          insertMode: "replace",
        },
        hover: { enabled: "on", above: false },
        // ── the design's editor rules ───────────────────────────────────────
        tabSize: 2,
        insertSpaces: true,
        detectIndentation: false,
        scrollBeyondLastLine: false,
        automaticLayout: true,
        fixedOverflowWidgets: true,
        padding: { top: 12, bottom: 12 },
        scrollbar: { verticalScrollbarSize: 12, horizontalScrollbarSize: 12 },
      }}
      className={className ?? "overflow-hidden rounded-lg border"}
    />
  );
};
