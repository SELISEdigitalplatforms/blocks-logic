import { useEffect } from "react";
import { act, render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Monaco, OnMount } from "@monaco-editor/react";
import { buildFunctionTypeDefs, FUNCTION_TYPES_PATH } from "../../utils/function-types";
import { findReuseHints } from "../../utils/reuse-hints";

// `loader.init()` with `makeCancelable`'s semantics: `cancel()` only flags the promise, which then
// rejects with the cancellation once the load settles. The `Editor` stub mounts straight away with
// a fake editor, and records the theme it was given.
const fake = vi.hoisted(() => {
  const CANCELATION = { type: "cancelation", msg: "operation is manually canceled" };
  const loads: { resolve: (monaco: unknown) => void; cancel: () => void }[] = [];
  const state = { cached: null as unknown, monaco: null as unknown, themes: [] as string[] };
  const init = vi.fn(() => {
    let canceled = false;
    const load = {} as (typeof loads)[number];
    const promise = new Promise((resolve, reject) => {
      load.resolve = (monaco) => (canceled ? reject(CANCELATION) : resolve(monaco));
    }) as Promise<unknown> & { cancel: () => void };
    load.cancel = vi.fn(() => {
      canceled = true;
    });
    promise.cancel = load.cancel;
    loads.push(load);
    return promise;
  });
  return { loads, state, init };
});

vi.mock("@monaco-editor/react", () => ({
  loader: { config: vi.fn(), init: fake.init, __getMonacoInstance: () => fake.state.cached },
  default: function EditorStub({
    theme,
    beforeMount,
    onMount,
  }: Readonly<{
    theme: string;
    beforeMount?: (monaco: Monaco) => void;
    onMount?: OnMount;
  }>) {
    fake.state.themes.push(theme);
    useEffect(() => {
      beforeMount?.(fake.state.monaco as Monaco);
      onMount?.(createFakeEditor() as unknown as Parameters<OnMount>[0], fake.state.monaco as Monaco);
    }, []); // eslint-disable-line react-hooks/exhaustive-deps -- mounts once, like Monaco's editor
    return <div data-testid="monaco-editor" />;
  },
}));

const theme = vi.hoisted(() => ({ resolvedTheme: "light" }));
vi.mock("@seliseblocks/genesis-os/hooks", async () => ({
  ...(await vi.importActual<object>("@seliseblocks/genesis-os/hooks")),
  useTheme: () => theme,
}));

import { CodeEditor } from "./code-editor";

const MODEL = { getLineContent: () => "" };

function createFakeEditor() {
  return {
    getModel: () => MODEL,
    updateOptions: vi.fn(),
    getContainerDomNode: () => document.createElement("div"),
    addCommand: vi.fn(),
    getAction: vi.fn(),
    onDidDispose: vi.fn(),
  };
}

const createFakeMonaco = () => {
  const javascriptDefaults = {
    setCompilerOptions: vi.fn(),
    setDiagnosticsOptions: vi.fn(),
    setEagerModelSync: vi.fn(),
    addExtraLib: vi.fn(() => ({ dispose: vi.fn() })),
  };
  return {
    typescript: {
      javascriptDefaults,
      ScriptTarget: { ESNext: 99 },
      ModuleKind: { ESNext: 99 },
      ModuleResolutionKind: { NodeJs: 2 },
    },
    languages: {
      registerCompletionItemProvider: vi.fn(() => ({ dispose: vi.fn() })),
      CompletionItemKind: { Property: 9 },
    },
    editor: { defineTheme: vi.fn(), setModelMarkers: vi.fn() },
    MarkerSeverity: { Warning: 4 },
    KeyMod: { Shift: 1, Alt: 2 },
    KeyCode: { KeyF: 3 },
  };
};

const REUSE_CODE = "setInterval(() => {}, 1000);";

let monaco: ReturnType<typeof createFakeMonaco>;
let unhandled: unknown[];
const onProcessRejection = (reason: unknown) => unhandled.push(reason);
const onWindowRejection = (event: PromiseRejectionEvent) => unhandled.push(event.reason);
let consoleError: ReturnType<typeof vi.spyOn>;

// Lets settled promises run their handlers and Node report whatever stayed unhandled.
const flush = () => act(() => new Promise((resolve) => setTimeout(resolve, 0)));

beforeEach(() => {
  monaco = createFakeMonaco();
  fake.state.monaco = monaco;
  fake.state.cached = null;
  fake.state.themes = [];
  fake.loads.length = 0;
  fake.init.mockClear();
  theme.resolvedTheme = "light";
  unhandled = [];
  process.on("unhandledRejection", onProcessRejection);
  window.addEventListener("unhandledrejection", onWindowRejection);
  consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
});

afterEach(() => {
  process.off("unhandledRejection", onProcessRejection);
  window.removeEventListener("unhandledrejection", onWindowRejection);
  consoleError.mockRestore();
});

describe("CodeEditor Monaco loading", () => {
  it("applies the defaults, ctx.env library, ctx. completions and reuse hints once loaded (H1)", async () => {
    render(<CodeEditor language="javascript" value={REUSE_CODE} envKeys={["API_KEY"]} />);
    expect(fake.init).toHaveBeenCalledTimes(1);
    // beforeMount configured the editor's own instance.
    expect(monaco.typescript.javascriptDefaults.setCompilerOptions).toHaveBeenCalled();
    expect(monaco.editor.defineTheme).toHaveBeenCalledWith("blocks-light", expect.any(Object));
    expect(monaco.languages.registerCompletionItemProvider).not.toHaveBeenCalled();

    await act(async () => fake.loads[0].resolve(monaco));

    expect(monaco.typescript.javascriptDefaults.addExtraLib).toHaveBeenCalledWith(
      buildFunctionTypeDefs(["API_KEY"]),
      FUNCTION_TYPES_PATH,
    );
    expect(monaco.languages.registerCompletionItemProvider).toHaveBeenCalledWith(
      "javascript",
      expect.objectContaining({ triggerCharacters: ["."] }),
    );
    const hints = findReuseHints(REUSE_CODE);
    expect(hints.length).toBeGreaterThan(0);
    expect(monaco.editor.setModelMarkers).toHaveBeenLastCalledWith(
      MODEL,
      "blocks-reuse-hints",
      hints.map((hint) => expect.objectContaining({ message: hint.message, startLineNumber: hint.line })),
    );
    expect(consoleError).not.toHaveBeenCalled();
  });

  it("cancels the load with no unhandled rejection when it unmounts first (H3)", async () => {
    const { unmount } = render(<CodeEditor language="javascript" value="" />);
    unmount();
    expect(fake.loads[0].cancel).toHaveBeenCalledTimes(1);

    fake.loads[0].resolve(monaco);
    await flush();

    expect(unhandled).toEqual([]);
    expect(consoleError).not.toHaveBeenCalled();
    expect(monaco.languages.registerCompletionItemProvider).not.toHaveBeenCalled();
  });

  it("handles both Code-tab editors unmounting together while loading (C3)", async () => {
    const { unmount } = render(
      <>
        <CodeEditor language="javascript" value="" />
        <CodeEditor language="json" value="{}" />
      </>,
    );
    expect(fake.loads).toHaveLength(2);
    unmount();

    fake.loads.forEach((load) => load.resolve(monaco));
    await flush();

    fake.loads.forEach((load) => expect(load.cancel).toHaveBeenCalledTimes(1));
    expect(unhandled).toEqual([]);
    expect(consoleError).not.toHaveBeenCalled();
  });

  it("reuses the cached instance on return to the Code tab without loading again (H6)", () => {
    fake.state.cached = monaco;
    render(<CodeEditor language="javascript" value="" envKeys={["API_KEY"]} />);

    expect(fake.init).not.toHaveBeenCalled();
    expect(monaco.typescript.javascriptDefaults.addExtraLib).toHaveBeenCalledTimes(1);
    expect(monaco.languages.registerCompletionItemProvider).toHaveBeenCalledTimes(1);
  });

  it("keeps the function themes in light and dark (H11)", () => {
    fake.state.cached = monaco;
    const { rerender } = render(<CodeEditor language="javascript" value="" />);
    theme.resolvedTheme = "dark";
    rerender(<CodeEditor language="javascript" value="" />);

    expect(fake.state.themes[0]).toBe("blocks-light");
    expect(fake.state.themes.at(-1)).toBe("blocks-dark");
  });
});
