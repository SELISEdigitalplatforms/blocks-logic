import React from "react";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FormBuilder } from "./form-builder";
import type { FieldSchema } from "./form-field.types";
import { FIELD_COMPONENTS_REGISTRY } from "./fields";
import type { FieldComponentProps } from "./form-field.types";

// Controlled wrapper so onChange updates flow back into the form.
const Harness = ({
  fields,
  initial = {},
  onChangeSpy,
}: {
  fields: FieldSchema[];
  initial?: Record<string, unknown>;
  onChangeSpy?: (d: Record<string, unknown>) => void;
}) => {
  const [data, setData] = React.useState<Record<string, unknown>>(initial);
  return (
    <FormBuilder
      fields={fields}
      data={data}
      onChange={(d) => {
        onChangeSpy?.(d);
        setData(d);
      }}
    />
  );
};

const render = (
  fields: FieldSchema[],
  initial?: Record<string, unknown>,
  onChangeSpy?: (d: Record<string, unknown>) => void,
) =>
  renderWithProviders(
    <Harness fields={fields} initial={initial} onChangeSpy={onChangeSpy} />,
  );

describe("FormBuilder", () => {
  it("renders labels, required markers and info tooltips", () => {
    render([
      {
        id: "name",
        key: "name",
        type: "text",
        label: "Name",
        required: true,
        info: "The name",
      } as FieldSchema,
    ]);
    expect(screen.getByText("Name")).toBeTruthy();
  });

  it("renders an unknown field type gracefully", () => {
    render([
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      { id: "x", key: "x", type: "does-not-exist" as any, label: "X" },
    ]);
    expect(screen.getByText(/Unknown field type/)).toBeTruthy();
  });

  it("edits a text field and reports the change", () => {
    const spy = vi.fn();
    render(
      [{ id: "name", key: "name", type: "text", label: "Name" } as FieldSchema],
      {},
      spy,
    );
    const input = document.getElementById("name") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "Ada" } });
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ name: "Ada" }));
  });

  it("converts number field input to a number", () => {
    const spy = vi.fn();
    render(
      [{ id: "n", key: "n", type: "number", label: "N" } as FieldSchema],
      {},
      spy,
    );
    const input = document.getElementById("n") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "5" } });
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ n: 5 }));
  });

  it("toggles a switch field", () => {
    const spy = vi.fn();
    render(
      [{ id: "s", key: "s", type: "switch", label: "S" } as FieldSchema],
      { s: false },
      spy,
    );
    fireEvent.click(document.getElementById("s") as HTMLElement);
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ s: true }));
  });

  it("hides fields whose dependency is not satisfied", () => {
    render(
      [
        { id: "mode", key: "mode", type: "text", label: "Mode" } as FieldSchema,
        {
          id: "extra",
          key: "extra",
          type: "text",
          label: "Extra",
          dependsOn: { key: "mode", value: "advanced" },
        } as FieldSchema,
      ],
      { mode: "basic" },
    );
    expect(screen.queryByText("Extra")).toBeNull();
  });

  it("shows fields whose dependency is satisfied", () => {
    render(
      [
        { id: "mode", key: "mode", type: "text", label: "Mode" } as FieldSchema,
        {
          id: "extra",
          key: "extra",
          type: "text",
          label: "Extra",
          dependsOn: { key: "mode", value: "advanced" },
        } as FieldSchema,
      ],
      { mode: "advanced" },
    );
    expect(screen.getByText("Extra")).toBeTruthy();
  });

  it("hides a field when its hidden function returns true", () => {
    render(
      [
        {
          id: "h",
          key: "h",
          type: "text",
          label: "Hidden",
          hidden: () => true,
        } as FieldSchema,
      ],
      {},
    );
    expect(screen.queryByText("Hidden")).toBeNull();
  });

  it("renders a display field read-only from displayValue", () => {
    render(
      [
        {
          id: "d",
          key: "d",
          type: "display",
          label: "Doc",
          displayValue: () => "hello world",
        } as FieldSchema,
      ],
      {},
    );
    expect(screen.getByText("hello world")).toBeTruthy();
  });

  it("loads async select options and selects one", async () => {
    const spy = vi.fn();
    const options = vi
      .fn()
      .mockResolvedValue([
        { value: "a", label: "Option A" },
        { value: "b", label: "Option B" },
      ]);
    render(
      [
        {
          id: "sel",
          key: "sel",
          type: "select",
          label: "Sel",
          options,
        } as FieldSchema,
      ],
      {},
      spy,
    );
    await waitFor(() => expect(options).toHaveBeenCalled());
  });

  it("runs a field onChange side-effect and merges the result", () => {
    const spy = vi.fn();
    render(
      [
        {
          id: "a",
          key: "a",
          type: "text",
          label: "A",
          onChange: (value: unknown) => ({ derived: `${value}-x` }),
        } as FieldSchema,
      ],
      {},
      spy,
    );
    const input = document.getElementById("a") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "v" } });
    expect(spy).toHaveBeenCalledWith(
      expect.objectContaining({ a: "v", derived: "v-x" }),
    );
  });

  it("strips transient keys from the persisted payload", () => {
    const spy = vi.fn();
    render(
      [
        {
          id: "keep",
          key: "keep",
          type: "text",
          label: "Keep",
        } as FieldSchema,
        {
          id: "temp",
          key: "temp",
          type: "text",
          label: "Temp",
          transient: true,
        } as FieldSchema,
      ],
      { temp: "should-not-persist" },
      spy,
    );
    const input = document.getElementById("keep") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "x" } });
    const payload = spy.mock.calls[0][0];
    expect(payload.keep).toBe("x");
    expect("temp" in payload).toBe(false);
  });

  it("keeps every write when two forced switches save in the same commit", async () => {
    const spy = vi.fn();
    const forced = (id: string): FieldSchema => ({
      id,
      key: id,
      type: "switch",
      label: id,
      locked: async () => true,
    });
    render([forced("first"), forced("second")], {}, spy);
    await waitFor(() =>
      expect(spy).toHaveBeenLastCalledWith(expect.objectContaining({ first: true, second: true })),
    );
  });
});

describe("FormBuilder json-fields patch path", () => {
  const jsonFields = (extra: Partial<FieldSchema> = {}): FieldSchema => ({
    id: "ops",
    key: "fields",
    type: "json-fields",
    label: "Fields",
    transient: true,
    rows: () => [
      { key: "email", kind: "text", defaultValue: "" },
      { key: "roles", kind: "list", defaultValue: [], options: [{ value: "editor", label: "Editor" }] },
    ],
    ...extra,
  });

  it("H10: writes only the patched keys, resets dependants of each and never saves the field's key", () => {
    const spy = vi.fn();
    const sideEffect = vi.fn(() => ({ audit: "changed" }));
    render(
      [
        jsonFields({ onChange: sideEffect }),
        {
          id: "confirm",
          key: "confirm",
          type: "text",
          label: "Confirm",
          defaultValue: "",
          dependsOn: { key: "email", value: ["a@x.io", "b@x.io"], operator: "in" },
        } as FieldSchema,
      ],
      { email: "a@x.io", confirm: "yes", roles: ["editor"], other: 1 },
      spy,
    );
    fireEvent.change(screen.getByRole("textbox", { name: "email" }), { target: { value: "b@x.io" } });
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0][0]).toEqual({
      email: "b@x.io",
      confirm: "",
      roles: ["editor"],
      other: 1,
      audit: "changed",
    });
    expect("fields" in spy.mock.calls[0][0]).toBe(false);
    expect(sideEffect).toHaveBeenCalledWith(
      { email: "b@x.io" },
      expect.objectContaining({ email: "b@x.io" }),
      expect.anything(),
    );
  });

  it("H10: does not save the field's own key even when it is in the data", () => {
    const spy = vi.fn();
    render([jsonFields()], { fields: "stale", email: "" }, spy);
    fireEvent.change(screen.getByRole("textbox", { name: "email" }), { target: { value: "x" } });
    expect(spy.mock.calls[0][0]).toEqual({ email: "x" });
  });

  it("C9: the single-key path is unchanged for other fields next to a json-fields field", () => {
    const spy = vi.fn();
    render(
      [jsonFields(), { id: "name", key: "name", type: "text", label: "Name" } as FieldSchema],
      { email: "a" },
      spy,
    );
    fireEvent.change(document.getElementById("name") as HTMLInputElement, { target: { value: "Ada" } });
    expect(spy.mock.calls[0][0]).toEqual({ email: "a", name: "Ada" });
  });

  it("§5 / D2: a keystroke in a row renders each other field once, as any keystroke does today", () => {
    const original = FIELD_COMPONENTS_REGISTRY.text;
    const renders = vi.fn();
    FIELD_COMPONENTS_REGISTRY.text = (props) => {
      renders(props.field.key);
      return original(props);
    };
    try {
      render(
        [jsonFields(), { id: "name", key: "name", type: "text", label: "Name" } as FieldSchema],
        {},
      );
      renders.mockClear();
      fireEvent.change(screen.getByRole("textbox", { name: "email" }), { target: { value: "a" } });
      expect(renders).toHaveBeenCalledTimes(1);
      renders.mockClear();
      // The same cost as a keystroke in an ordinary field.
      fireEvent.change(document.getElementById("name") as HTMLInputElement, { target: { value: "b" } });
      expect(renders).toHaveBeenCalledTimes(1);
    } finally {
      FIELD_COMPONENTS_REGISTRY.text = original;
    }
  });

  /** Swaps the json-fields component for a stub that sends `patch` on click. */
  const withPatchStub = (patch: unknown, run: () => void) => {
    const original = FIELD_COMPONENTS_REGISTRY["json-fields"];
    FIELD_COMPONENTS_REGISTRY["json-fields"] = ({ onChange }: FieldComponentProps) => (
      <button type="button" onClick={() => onChange(patch)}>
        patch
      </button>
    );
    try {
      run();
    } finally {
      FIELD_COMPONENTS_REGISTRY["json-fields"] = original;
    }
  };

  it("H10: resets the dependants of every key in a multi-key patch", () => {
    const spy = vi.fn();
    withPatchStub({ a: "2", b: "2" }, () => {
      render(
        [
          jsonFields(),
          { id: "x", key: "x", type: "text", label: "X", defaultValue: "", dependsOn: { key: "a", value: "2" } } as FieldSchema,
          { id: "y", key: "y", type: "text", label: "Y", defaultValue: "", dependsOn: { key: "b", value: "2" } } as FieldSchema,
        ],
        { a: "1", b: "1", x: "kept?", y: "kept?" },
        spy,
      );
      fireEvent.click(screen.getByRole("button", { name: "patch" }));
    });
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0][0]).toEqual({ a: "2", b: "2", x: "", y: "" });
  });

  it("H10: an empty or non-object patch saves nothing", () => {
    for (const patch of [{}, null, "text", ["a"]]) {
      const spy = vi.fn();
      withPatchStub(patch, () => {
        const view = render([jsonFields()], { email: "a" }, spy);
        fireEvent.click(screen.getByRole("button", { name: "patch" }));
        view.unmount();
      });
      expect(spy).not.toHaveBeenCalled();
    }
  });
});
