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
import { ExpressionListField } from "./expression-list-field";
import { FixedKeyValuePairsField } from "./fixed-key-value-pairs-field";
import { KeyTypeValueField } from "./key-type-value-field";
import { KeyValuePairsField } from "./key-value-pairs-field";
import { showsVariablePicker } from "./secret-picker";
import { TextField } from "./text-field";
import { TextareaField } from "./textarea-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { tenantId: "pk", workflowId: "wf", nodeId: "n1" };

const field = (extra: Partial<FieldSchema> = {}): FieldSchema => ({
  id: "f",
  key: "apiKey",
  label: "API key",
  type: "text",
  ...extra,
});

const pickers = () => screen.queryAllByRole("button", { name: /insert a configuration variable/i });

/**
 * Opens a picker and picks the first mock secret, once the secrets have loaded. Takes a getter: the
 * disabled placeholder trigger is swapped for a new element when the list arrives.
 */
const pick = async (user: ReturnType<typeof userEvent.setup>, trigger: () => HTMLElement) => {
  await vi.waitFor(() => expect(trigger().hasAttribute("disabled")).toBe(false));
  await user.click(trigger());
  await user.click(await screen.findByRole("menuitem", { name: "stripe-api-key" }));
};

describe("showsVariablePicker", () => {
  it("is on by default for eligible types only", () => {
    for (const type of [
      "text",
      "textarea",
      "expression",
      "key-value-pairs",
      "fixed-key-value-pairs",
      "key-type-value-pairs",
      "expression-list",
    ] as const) {
      expect(showsVariablePicker(field({ type }))).toBe(true);
    }
    for (const type of [
      "select",
      "number",
      "switch",
      "json-code-editor",
      "conditions",
      "schema-fields",
    ] as const) {
      expect(showsVariablePicker(field({ type }))).toBe(false);
    }
  });

  it("is off when opted out, read-only or disabled", () => {
    expect(showsVariablePicker(field({ variablePicker: false }))).toBe(false);
    expect(showsVariablePicker(field(), true)).toBe(false);
    expect(showsVariablePicker(field({ disabled: true }))).toBe(false);
  });

  it("is opted out on the proxy node's path and query params", () => {
    const params = NodeSchemaActionProxy.schema.parameters;
    expect(params.find((p) => p.key === "pathParams")?.variablePicker).toBe(false);
    expect(params.find((p) => p.key === "queryParams")?.variablePicker).toBe(false);
  });
});

describe("secret picker on workflow fields", () => {
  it("renders on a text field by default and replaces the value with the token", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <TextField field={field()} value="typed" onChange={onChange} data={{}} config={cfg} />,
    );

    expect(pickers()).toHaveLength(1);
    await pick(user, () => pickers()[0]);
    expect(onChange).toHaveBeenLastCalledWith("{{$VAR.stripe-api-key}}");
  });

  it("is hidden when opted out, read-only, or copyable", () => {
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
        field={field({ copyable: true })}
        value=""
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );
    expect(pickers()).toHaveLength(0);
  });

  it("renders on a textarea", () => {
    renderWithProviders(
      <TextareaField
        field={field({ type: "textarea" })}
        value=""
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );
    expect(pickers()).toHaveLength(1);
  });

  it("puts one picker on each key-value row and replaces only that row's value", async () => {
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

    expect(pickers()).toHaveLength(2);
    await pick(user, () => screen.getByRole("button", { name: /into Headers row 2 value/ }));
    expect(onChange).toHaveBeenLastCalledWith({
      Authorization: "x",
      Accept: "{{$VAR.stripe-api-key}}",
    });
  });

  it("puts a picker on each fixed-key value", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <FixedKeyValuePairsField
        field={field({ type: "fixed-key-value-pairs", label: "Map", fixedKeys: ["name", "code"] })}
        value={{ name: "a", code: "b" }}
        onChange={onChange}
        data={{}}
        config={cfg}
      />,
    );

    expect(pickers()).toHaveLength(2);
    await pick(user, () => screen.getByRole("button", { name: /into Map code value/ }));
    expect(onChange).toHaveBeenLastCalledWith({ name: "a", code: "{{$VAR.stripe-api-key}}" });
  });

  it("offers the picker on string rows of key-type-value pairs only", () => {
    renderWithProviders(
      <KeyTypeValueField
        field={field({ type: "key-type-value-pairs", label: "Fields" })}
        value={[
          { key: "a", type: "string", value: "" },
          { key: "b", type: "number", value: "1" },
        ]}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
      />,
    );

    expect(pickers()).toHaveLength(1);
    expect(screen.getByRole("button", { name: /into Fields row 1 value/ })).toBeTruthy();
  });

  it("puts a picker on each expression-list item", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    renderWithProviders(
      <ExpressionListField
        field={field({ type: "expression-list", label: "Items" })}
        value={["one", "two"]}
        onChange={onChange}
        data={{}}
        config={cfg}
      />,
    );

    expect(pickers()).toHaveLength(2);
    await pick(user, () => screen.getByRole("button", { name: /into Items item 1/ }));
    expect(onChange).toHaveBeenLastCalledWith(["{{$VAR.stripe-api-key}}", "two"]);
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
