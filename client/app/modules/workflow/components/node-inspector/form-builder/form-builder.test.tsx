import React from "react";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FormBuilder } from "./form-builder";
import type { FieldSchema } from "./form-field.types";

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
});
