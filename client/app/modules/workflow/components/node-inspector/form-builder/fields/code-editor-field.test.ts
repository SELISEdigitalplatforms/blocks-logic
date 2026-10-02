import { describe, expect, it, vi } from "vitest";
import type { Monaco } from "@monaco-editor/react";
import { insertDroppedText } from "./code-editor-field";

vi.mock("@seliseblocks/genesis-os/hooks", () => ({ useTheme: () => ({ resolvedTheme: "light" }) }));

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
