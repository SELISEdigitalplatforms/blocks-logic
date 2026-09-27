import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test-utils/test-providers/render";

vi.mock("@/services/secret.service", async () => {
  const actual = await import("@/services/secret.service");
  const { mockSecretService } = await import("@/modules/proxy/test-support/mock-secret-service");
  return { ...actual, secretService: mockSecretService };
});

import { FieldSchema } from "../form-field.types";
import { NodeSchemaActionProxy } from "../../../node-schemas/node-schema-action-proxy";
import { ConditionsField } from "./conditions-field";
import { ExpressionListField } from "./expression-list-field";
import { FixedKeyValuePairsField } from "./fixed-key-value-pairs-field";
import { GraphqlCodeEditor } from "./graphql-code-editor-field";
import { JsonCodeEditor } from "./json-code-editor-field";
import { KeyTypeValueField } from "./key-type-value-field";
import { KeyValuePairsField } from "./key-value-pairs-field";
import { showsVariablePicker } from "./secret-picker";
import { TextField } from "./text-field";
import { TextareaField } from "./textarea-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const TOKEN = "{{$VAR.stripe-api-key}}";

const field = (extra: Partial<FieldSchema> = {}): FieldSchema => ({
  id: "f",
  key: "apiKey",
  label: "API key",
  type: "text",
  ...extra,
});

const pickers = () => screen.queryAllByRole("button", { name: /insert a configuration variable/i });

/** Opens a picker's popover and picks the first mock secret once the list has loaded. */
const pick = async (user: ReturnType<typeof userEvent.setup>, trigger: HTMLElement) => {
  await user.click(trigger);
  await user.click(await screen.findByRole("option", { name: "stripe-api-key" }));
};

describe("showsVariablePicker", () => {
  it("is on by default, off when opted out, read-only or disabled", () => {
    expect(showsVariablePicker(field())).toBe(true);
    expect(showsVariablePicker(field({ type: "json-code-editor" }))).toBe(true);
    expect(showsVariablePicker(field({ variablePicker: false }))).toBe(false);
    expect(showsVariablePicker(field(), true)).toBe(false);
    expect(showsVariablePicker(field({ disabled: true }))).toBe(false);
  });

  it("is not opted out anywhere on the proxy node", () => {
    for (const param of NodeSchemaActionProxy.schema.parameters) {
      expect(param.variablePicker).not.toBe(false);
    }
  });
});

describe("secret picker on workflow fields", () => {
  it("replaces the field's text with the picked token", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <TextField field={field()} value="Bearer " onChange={onChange} data={{}} config={cfg} />,
    );

    expect(pickers()).toHaveLength(1);
    await pick(user, pickers()[0]);
    expect(onChange).toHaveBeenLastCalledWith(TOKEN);
  });

  it("lists the secrets in a popover", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <TextField field={field()} value="" onChange={vi.fn()} data={{}} config={cfg} />,
    );

    await user.click(pickers()[0]);
    expect(await screen.findByText("Configuration variables")).toBeTruthy();
    expect(screen.getByRole("option", { name: "stripe-api-key" })).toBeTruthy();
  });

  it("is hidden when opted out, read-only, disabled, or copyable", () => {
    const { rerender } = renderWithProviders(
      <TextField
        field={field({ variablePicker: false })}
        value=""
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );
    expect(pickers()).toHaveLength(0);

    rerender(
      <TextField field={field()} value="" onChange={vi.fn()} data={{}} config={cfg} readOnly />,
    );
    expect(pickers()).toHaveLength(0);

    rerender(
      <TextField
        field={field({ disabled: true })}
        value=""
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );
    expect(pickers()).toHaveLength(0);

    rerender(
      <TextField
        field={field({ copyable: true })}
        value=""
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );
    expect(pickers()).toHaveLength(0);
  });

  it.each([
    ["textarea", TextareaField],
    ["json-code-editor", JsonCodeEditor],
    ["graphql-code-editor", GraphqlCodeEditor],
  ] as const)("renders on a %s", (type, Component) => {
    renderWithProviders(
      <Component field={field({ type })} value="" onChange={vi.fn()} data={{}} config={cfg} />,
    );
    expect(pickers()).toHaveLength(1);
  });

  it("puts a picker on each key-value key and value, changing only that input", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <KeyValuePairsField
        field={field({ type: "key-value-pairs", label: "Headers" })}
        value={{ Authorization: "x", Accept: "json" }}
        onChange={onChange}
        data={{}}
        config={cfg}
      />,
    );

    expect(pickers()).toHaveLength(4);
    await pick(user, screen.getByRole("button", { name: /into Headers row 2 value/ }));
    expect(onChange).toHaveBeenLastCalledWith({ Authorization: "x", Accept: TOKEN });
    expect(screen.getByRole("button", { name: /into Headers row 1 key/ })).toBeTruthy();
  });

  it("puts a picker on each fixed-key value", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <FixedKeyValuePairsField
        field={field({ type: "fixed-key-value-pairs", label: "Map", fixedKeys: ["name", "code"] })}
        value={{ name: "a", code: "" }}
        onChange={onChange}
        data={{}}
        config={cfg}
      />,
    );

    expect(pickers()).toHaveLength(2);
    await pick(user, screen.getByRole("button", { name: /into Map code value/ }));
    expect(onChange).toHaveBeenLastCalledWith({ name: "a", code: TOKEN });
  });

  it("puts a picker on key-type-value keys and non-array values", () => {
    renderWithProviders(
      <KeyTypeValueField
        field={field({ type: "key-type-value-pairs", label: "Fields" })}
        value={[
          { key: "a", type: "string", value: "" },
          { key: "b", type: "number", value: "1" },
          { key: "c", type: "array", value: [] },
        ]}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );

    expect(pickers()).toHaveLength(5);
    expect(screen.getByRole("button", { name: /into Fields row 2 value/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /into Fields row 3 value/ })).toBeNull();
  });

  it("puts a picker on both condition operands", () => {
    renderWithProviders(
      <ConditionsField
        field={field({ type: "conditions", label: "Conditions" })}
        value={[{ left: "a", operator: "equals", right: "b", type: "string" }]}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );

    expect(screen.getByRole("button", { name: /condition 1 left operand/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: /condition 1 right operand/ })).toBeTruthy();
  });

  it("puts a picker on each expression-list item", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <ExpressionListField
        field={field({ type: "expression-list", label: "Items" })}
        value={["", "two"]}
        onChange={onChange}
        data={{}}
        config={cfg}
      />,
    );

    expect(pickers()).toHaveLength(2);
    await pick(user, screen.getByRole("button", { name: /into Items item 1/ }));
    expect(onChange).toHaveBeenLastCalledWith([TOKEN, "two"]);
  });

  // Every field that wraps an input in ExpressionHighlighter, with a value that renders an input.
  const HIGHLIGHTED_FIELDS = [
    ["text", TextField, "a"],
    ["textarea", TextareaField, "a"],
    ["json-code-editor", JsonCodeEditor, "{}"],
    ["graphql-code-editor", GraphqlCodeEditor, "query"],
    ["expression-list", ExpressionListField, ["a"]],
    ["key-value-pairs", KeyValuePairsField, { a: "1" }],
    ["fixed-key-value-pairs", FixedKeyValuePairsField, { a: "1" }],
    ["key-type-value-pairs", KeyTypeValueField, [{ key: "a", type: "string", value: "1" }]],
    ["conditions", ConditionsField, [{ left: "a", operator: "equals", right: "b" }]],
  ] as const;

  const renderField = (
    [type, Component, value]: (typeof HIGHLIGHTED_FIELDS)[number],
    props: { readOnly?: boolean; disabled?: boolean; variablePicker?: boolean } = {},
  ) => {
    const { disabled, ...rest } = props;
    const Field = Component as React.ComponentType<Record<string, unknown>>;
    return renderWithProviders(
      <Field
        field={field({ type, fixedKeys: ["a"], disabled })}
        value={value}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        {...rest}
      />,
    );
  };

  it.each(HIGHLIGHTED_FIELDS)("shows the key button on an editable %s", (...entry) => {
    renderField(entry);
    expect(pickers().length).toBeGreaterThan(0);
  });

  it.each(HIGHLIGHTED_FIELDS)("never shows the key button on a read-only %s", (...entry) => {
    renderField(entry, { readOnly: true });
    expect(pickers()).toHaveLength(0);
  });

  it.each(HIGHLIGHTED_FIELDS)("never shows the key button on a disabled %s", (...entry) => {
    renderField(entry, { disabled: true });
    expect(pickers()).toHaveLength(0);
  });

  it.each(HIGHLIGHTED_FIELDS)("hides the key button on a %s reused with variablePicker={false}", (...entry) => {
    renderField(entry, { variablePicker: false });
    expect(pickers()).toHaveLength(0);
  });

  it("is hidden on every row when the field is read-only", () => {
    renderWithProviders(
      <KeyValuePairsField
        field={field({ type: "key-value-pairs" })}
        value={{ a: "1" }}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly
      />,
    );
    expect(pickers()).toHaveLength(0);
  });
});
