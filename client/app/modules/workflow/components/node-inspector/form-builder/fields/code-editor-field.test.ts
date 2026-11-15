import { createElement } from "react";
import { act, render } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from "vitest";
import type { Monaco } from "@monaco-editor/react";
import type { FieldProps } from "../form-field.types";

// `loader.init()` with `makeCancelable`'s semantics from @monaco-editor/loader: `cancel()` only
// flags the promise, which then rejects with the cancellation once the load settles. The `Editor`
// stub records the theme it was given and renders nothing.
const fake = vi.hoisted(() => {
  const CANCELATION = { type: "cancelation", msg: "operation is manually canceled" };
  const loads: { resolve: (monaco: unknown) => void; cancel: Mock<() => void> }[] = [];
  const themes: string[] = [];
  const state = { loads, themes, cached: null as unknown, init: vi.fn() };
  state.init.mockImplementation(() => {
    let canceled = false;
    const load = {} as (typeof loads)[number];
    const promise = new Promise((resolve, reject) => {
      load.resolve = (monaco) => (canceled ? reject(CANCELATION) : resolve(monaco));
    }) as Promise<unknown> & { cancel: () => void };
    load.cancel = vi.fn<() => void>(() => {
      canceled = true;
    });
    promise.cancel = load.cancel;
    loads.push(load);
    return promise;
  });
  return state;
});

vi.mock("@monaco-editor/react", () => ({
  loader: { config: vi.fn(), init: fake.init, __getMonacoInstance: () => fake.cached },
  default: ({ theme }: Readonly<{ theme: string }>) => {
    fake.themes.push(theme);
    return null;
  },
}));

const theme = vi.hoisted(() => ({ resolvedTheme: "light" }));
vi.mock("@seliseblocks/genesis-os/hooks", () => ({ useTheme: () => theme }));

import { CodeEditorField, getCodeEditorSuggestionValues, insertDroppedText } from "./code-editor-field";

class FakeRange {
  constructor(
    public startLineNumber: number,
    public startColumn: number,
    public endLineNumber: number,
    public endColumn: number,
  ) {}
}

type Position = { lineNumber: number; column: number };

const READ_ONLY = 91;
const monaco = { Range: FakeRange, editor: { EditorOption: { readOnly: READ_ONLY } } } as unknown as Monaco;

// Single-line model: columns map directly to offsets.
const createEditor = ({
  value = "const x = ;",
  readOnly = false,
  target = { lineNumber: 1, column: 11 } as Position | null,
  cursor = { lineNumber: 1, column: 1 } as Position | null,
} = {}) => {
  const model = {
    getValueLength: () => value.length,
    getPositionAt: (offset: number) => ({ lineNumber: 1, column: offset + 1 }),
    getOffsetAt: (position: Position) => position.column - 1,
  };
  return {
    getOption: vi.fn((option: number) => (option === READ_ONLY ? readOnly : undefined)),
    getModel: () => model,
    getTargetAtClientPoint: vi.fn(() => (target ? { position: target } : null)),
    getPosition: () => cursor,
    executeEdits: vi.fn(),
    pushUndoStop: vi.fn(),
    setPosition: vi.fn(),
    focus: vi.fn(),
  };
};

const insert = (editor: ReturnType<typeof createEditor>, text: string) =>
  insertDroppedText(editor as unknown as Parameters<typeof insertDroppedText>[0], monaco, {
    clientX: 10,
    clientY: 20,
    dataTransfer: { getData: (type: string) => (type === "text/plain" ? text : "") } as unknown as DataTransfer,
  });

const edit = (column: number, text: string) => [
  { range: new FakeRange(1, column, 1, column), text, forceMoveMarkers: true },
];

describe("insertDroppedText", () => {
  it("inserts verbatim at the drop point and moves the cursor after it", () => {
    const editor = createEditor();
    expect(insert(editor, "$json.ItemId")).toBe(true);

    expect(editor.getTargetAtClientPoint).toHaveBeenCalledWith(10, 20);
    expect(editor.executeEdits).toHaveBeenCalledWith("input-panel-drop", edit(11, "$json.ItemId"));
    expect(editor.pushUndoStop).toHaveBeenCalled();
    expect(editor.setPosition).toHaveBeenCalledWith({ lineNumber: 1, column: 23 });
    expect(editor.focus).toHaveBeenCalled();
  });

  it("does not snippet-escape the text", () => {
    const editor = createEditor();
    insert(editor, '{{$node["X"].json.output.id}}');
    expect(editor.executeEdits).toHaveBeenCalledWith("input-panel-drop", edit(11, '{{$node["X"].json.output.id}}'));
  });

  it("falls back to the cursor when there is no target", () => {
    const editor = createEditor({ target: null, cursor: { lineNumber: 1, column: 3 } });
    insert(editor, "$json");
    expect(editor.executeEdits).toHaveBeenCalledWith("input-panel-drop", edit(3, "$json"));
  });

  it("falls back to the end of the model with no target and no cursor", () => {
    const editor = createEditor({ value: "abc", target: null, cursor: null });
    insert(editor, "$json");
    expect(editor.executeEdits).toHaveBeenCalledWith("input-panel-drop", edit(4, "$json"));
  });

  it("does nothing when read-only", () => {
    const editor = createEditor({ readOnly: true });
    expect(insert(editor, "$json")).toBe(false);
    expect(editor.executeEdits).not.toHaveBeenCalled();
  });

  it("does nothing when there is no text", () => {
    const editor = createEditor();
    expect(insert(editor, "")).toBe(false);
    expect(editor.executeEdits).not.toHaveBeenCalled();
  });
});

// ---- Monaco loading (useMonacoInstance) -------------------------------------------------------

const MONACO_INSTANCE = {
  languages: {
    registerCompletionItemProvider: vi.fn(() => ({ dispose: vi.fn() })),
    CompletionItemKind: { Variable: 4 },
  },
} as unknown as Monaco;
const registerProvider = MONACO_INSTANCE.languages.registerCompletionItemProvider as unknown as ReturnType<
  typeof vi.fn
>;

const fieldProps = (nodeId: string): FieldProps<string> => ({
  field: { id: nodeId, key: "code", type: "code-editor", language: "javascript" } as FieldProps["field"],
  value: "",
  onChange: vi.fn(),
  data: {},
  config: { upstreamKeys: ["ItemId"] } as unknown as FieldProps["config"],
});
const renderField = (nodeId: string) => createElement(CodeEditorField, { key: nodeId, ...fieldProps(nodeId) });

describe("CodeEditorField Monaco loading", () => {
  let unhandled: unknown[];
  const onProcessRejection = (reason: unknown) => unhandled.push(reason);
  const onWindowRejection = (event: PromiseRejectionEvent) => unhandled.push(event.reason);
  let consoleError: ReturnType<typeof vi.spyOn>;
  // Lets settled promises run their handlers and Node report whatever stayed unhandled.
  const flush = () => act(() => new Promise((resolve) => setTimeout(resolve, 0)));

  beforeEach(() => {
    fake.cached = null;
    fake.themes.length = 0;
    fake.loads.length = 0;
    fake.init.mockClear();
    registerProvider.mockClear();
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

  it("registers the $ completion provider once Monaco loads (H2)", async () => {
    render(renderField("a"));
    expect(registerProvider).not.toHaveBeenCalled();

    await act(async () => fake.loads[0].resolve(MONACO_INSTANCE));

    expect(registerProvider).toHaveBeenCalledTimes(1);
    const [language, provider] = registerProvider.mock.calls[0] as [
      string,
      { triggerCharacters: string[]; provideCompletionItems: (...args: unknown[]) => { suggestions: { label: string }[] } },
    ];
    expect(language).toBe("javascript");
    expect(provider.triggerCharacters).toEqual(["$", "."]);
    const model = { getLineContent: () => "return $" };
    const { suggestions } = provider.provideCompletionItems(model, { lineNumber: 1, column: 9 });
    expect(suggestions.map((item) => item.label)).toEqual(getCodeEditorSuggestionValues(["ItemId"]));
  });

  it("cancels the load with no unhandled rejection when it unmounts first (H4)", async () => {
    const { unmount } = render(renderField("a"));
    unmount();
    expect(fake.loads[0].cancel).toHaveBeenCalledTimes(1);

    fake.loads[0].resolve(MONACO_INSTANCE);
    await flush();

    expect(unhandled).toEqual([]);
    expect(consoleError).not.toHaveBeenCalled();
    expect(registerProvider).not.toHaveBeenCalled();
  });

  it("shows the last selected node's editor when switching fast on a cold load (C11)", async () => {
    const { rerender } = render(renderField("a"));
    rerender(renderField("b"));
    rerender(renderField("a"));
    // One load per mounted editor; the two left behind were canceled.
    expect(fake.loads).toHaveLength(3);
    expect(fake.loads.map((load) => load.cancel.mock.calls.length)).toEqual([1, 1, 0]);

    await act(async () => {
      fake.loads.forEach((load) => load.resolve(MONACO_INSTANCE));
    });
    await flush();

    expect(registerProvider).toHaveBeenCalledTimes(1);
    expect(unhandled).toEqual([]);
    expect(consoleError).not.toHaveBeenCalled();
  });

  it("keeps vs / vs-dark as the editor theme (H11)", () => {
    fake.cached = MONACO_INSTANCE;
    const { rerender } = render(renderField("a"));
    theme.resolvedTheme = "dark";
    rerender(renderField("a"));

    expect(fake.init).not.toHaveBeenCalled();
    expect(fake.themes[0]).toBe("vs");
    expect(fake.themes.at(-1)).toBe("vs-dark");
  });
});
