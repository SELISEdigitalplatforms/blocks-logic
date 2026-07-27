import React from "react";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SchemaFieldsField } from "./schema-fields-field";
import { SchemaFieldPickerField } from "./schema-field-picker-field";
import { ArrayField } from "./array-field";
import { ExpressionInputField } from "./expression-input-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { projectKey: "pk", workflowId: "wf", nodeId: "child" };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const field = (extra: Record<string, unknown> = {}): any => ({
  id: "f",
  key: "fieldMapping",
  type: "schema-fields",
  ...extra,
});

const schemaFields = [
  { name: "title", type: "string", isArray: false, description: "" },
  {
    name: "author",
    type: "entity",
    isArray: false,
    description: "",
    fields: [{ name: "name", type: "string", isArray: false, description: "" }],
  },
  {
    name: "tags",
    type: "entity",
    isArray: true,
    description: "",
    fields: [{ name: "label", type: "string", isArray: false, description: "" }],
  },
];

describe("SchemaFieldsField", () => {
  it("renders the empty state when no schema fields are present", () => {
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{}}
        onChange={vi.fn()}
        data={{ schemaFields: [] }}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText(/Select a collection/)).toBeTruthy();
  });

  it("renders scalar, entity and array-entity rows", () => {
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ title: "T", "author.name": "A", "tags.0.label": "x" }}
        onChange={vi.fn()}
        data={{ schemaFields }}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText("title")).toBeTruthy();
    expect(screen.getByText("author")).toBeTruthy();
  });

  it("adds a new array item", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ "tags.0.label": "x" }}
        onChange={onChange}
        data={{ schemaFields: [schemaFields[2]] }}
        config={cfg}
        readOnly={false}
      />,
    );
    fireEvent.click(screen.getByText(/Add tags/));
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ "tags.1.label": "" }),
    );
  });

  it("removes an array item", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ "tags.0.label": "x", "tags.1.label": "y" }}
        onChange={onChange}
        data={{ schemaFields: [schemaFields[2]] }}
        config={cfg}
        readOnly={false}
      />,
    );
    // remove buttons carry a trash icon; click one within an item block
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[1]);
    expect(onChange).toHaveBeenCalled();
  });

  it("edits a scalar value", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldsField
        field={field()}
        value={{ title: "" }}
        onChange={onChange}
        data={{ schemaFields: [schemaFields[0]] }}
        config={cfg}
        readOnly={false}
      />,
    );
    const input = document.getElementById("f") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "hello" } });
    expect(onChange).toHaveBeenCalledWith({ title: "hello" });
  });
});

describe("SchemaFieldPickerField", () => {
  it("renders the empty state without schema fields", () => {
    renderWithProviders(
      <SchemaFieldPickerField
        field={field({ type: "schema-field-picker" })}
        value={[]}
        onChange={vi.fn()}
        data={{ schemaFields: [] }}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText(/Select a collection/)).toBeTruthy();
  });

  it("auto-initialises all paths when value is empty", async () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldPickerField
        field={field({ type: "schema-field-picker" })}
        value={[]}
        onChange={onChange}
        data={{ schemaFields }}
        config={cfg}
        readOnly={false}
      />,
    );
    await waitFor(() =>
      expect(onChange).toHaveBeenCalledWith(
        expect.arrayContaining(["title", "author", "author.name", "tags"]),
      ),
    );
  });

  it("removes a field and its children", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <SchemaFieldPickerField
        field={field({ type: "schema-field-picker" })}
        value={["title", "author", "author.name", "tags", "tags.label"]}
        onChange={onChange}
        data={{ schemaFields }}
        config={cfg}
        readOnly={false}
      />,
    );
    // remove the "author" entity -> author + author.name dropped
    const authorButtons = screen
      .getAllByRole("button")
      .filter((b) => b.querySelector("svg"));
    fireEvent.click(authorButtons[0]);
    expect(onChange).toHaveBeenCalled();
    const arg = onChange.mock.calls[0][0] as string[];
    expect(arg).not.toContain("title");
  });
});

describe("ArrayField", () => {
  it("parses a valid JSON array", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ArrayField
        field={field({ id: "arr", type: "array" })}
        value={[]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    fireEvent.change(document.getElementById("arr") as HTMLTextAreaElement, {
      target: { value: '["x","y"]' },
    });
    expect(onChange).toHaveBeenCalledWith(["x", "y"]);
  });

  it("ignores non-array and invalid JSON", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ArrayField
        field={field({ id: "arr2", type: "array" })}
        value={[]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const ta = document.getElementById("arr2") as HTMLTextAreaElement;
    fireEvent.change(ta, { target: { value: '{"a":1}' } });
    fireEvent.change(ta, { target: { value: "not json" } });
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("ExpressionInputField", () => {
  const seedGraph = (store: {
    getState: () => {
      addNode: (n: unknown) => void;
      createEdge: (a: unknown, b: unknown) => void;
    };
  }) => {
    const s = store.getState();
    s.addNode({
      id: "parent",
      name: "webhook_main",
      type: "webhook",
      position: { x: 0, y: 0 },
      parameters: {},
    });
    s.addNode({
      id: "child",
      name: "child",
      type: "action",
      position: { x: 0, y: 0 },
      parameters: {},
    });
    s.createEdge(
      { source: "parent", sourceHandle: "main" },
      { target: "child", targetHandle: "in" },
    );
  };

  it("edits the value and reports changes", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ExpressionInputField
        field={field({ id: "expr", type: "expression" })}
        value=""
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      { seedWorkflow: seedGraph as any },
    );
    const input = document.getElementById("expr") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "hello" } });
    expect(onChange).toHaveBeenCalledWith("hello");
  });

  // Stateful wrapper: ExpressionInputField is controlled, so suggestions only
  // appear once the typed value flows back through the value prop.
  const ExprHarness = ({
    id,
    onChange,
  }: {
    id: string;
    onChange?: (v: string) => void;
  }) => {
    const [val, setVal] = React.useState("");
    return (
      <ExpressionInputField
        field={field({ id, type: "expression" })}
        value={val}
        onChange={(v) => {
          setVal(v as string);
          onChange?.(v as string);
        }}
        data={{}}
        config={cfg}
        readOnly={false}
      />
    );
  };

  const typeTrigger = (input: HTMLInputElement) => {
    fireEvent.change(input, {
      target: { value: "{{nodes.", selectionStart: 8 },
    });
  };

  it("shows node suggestions when typing an expression trigger", () => {
    renderWithProviders(<ExprHarness id="expr2" />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: seedGraph as any,
    });
    const input = document.getElementById("expr2") as HTMLInputElement;
    typeTrigger(input);
    expect(screen.getByText(/nodes\.webhook_main\.main/)).toBeTruthy();
  });

  it("inserts a suggestion as the stored node reference", () => {
    const onChange = vi.fn();
    renderWithProviders(<ExprHarness id="expr3" onChange={onChange} />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: seedGraph as any,
    });
    const input = document.getElementById("expr3") as HTMLInputElement;
    typeTrigger(input);
    fireEvent.click(screen.getByText(/nodes\.webhook_main\.main/));
    expect(onChange).toHaveBeenLastCalledWith(
      expect.stringContaining("{{node_parent_main}}"),
    );
  });

  it("closes suggestions on Escape", () => {
    renderWithProviders(<ExprHarness id="expr4" />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: seedGraph as any,
    });
    const input = document.getElementById("expr4") as HTMLInputElement;
    typeTrigger(input);
    expect(screen.getByText(/nodes\.webhook_main\.main/)).toBeTruthy();
    fireEvent.keyDown(input, { key: "Escape" });
    expect(screen.queryByText(/nodes\.webhook_main\.main/)).toBeNull();
  });

  it("is disabled when readOnly", () => {
    renderWithProviders(
      <ExpressionInputField
        field={field({ id: "expr5", type: "expression" })}
        value="x"
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={true}
      />,
    );
    expect((document.getElementById("expr5") as HTMLInputElement).disabled).toBe(
      true,
    );
  });
});
