import React from "react";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { KeyValueField } from "./key-value-field";
import { KeyValuePairsField } from "./key-value-pairs-field";
import { FixedKeyValuePairsField } from "./fixed-key-value-pairs-field";
import { KeyTypeValueField } from "./key-type-value-field";
import { TabWithTextField } from "./tab-with-text-field";
import { ConditionsField } from "./conditions-field";
import { SelectWithDescriptionField } from "./select-with-description-field";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const cfg: any = { projectKey: "pk", workflowId: "wf", nodeId: "n1" };

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const field = (extra: Record<string, unknown> = {}): any => ({
  id: "f",
  key: "f",
  type: "text",
  ...extra,
});

describe("KeyValueField", () => {
  it("renders a JSON editor and parses valid input", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyValueField
        field={field({ id: "kv" })}
        value={{ a: 1 }}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const ta = document.getElementById("kv") as HTMLTextAreaElement;
    fireEvent.change(ta, { target: { value: '{"b":2}' } });
    expect(onChange).toHaveBeenCalledWith({ b: 2 });
  });

  it("ignores invalid JSON", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyValueField
        field={field({ id: "kv2" })}
        value={{}}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    fireEvent.change(document.getElementById("kv2") as HTMLTextAreaElement, {
      target: { value: "not json" },
    });
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("KeyValuePairsField", () => {
  it("shows an add button when empty and adds a pair", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyValuePairsField
        field={field({ id: "kvp" })}
        value={{}}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    fireEvent.click(screen.getByText("Add Field"));
    // after adding, key/value inputs appear
    expect(document.getElementById("kvp-key-0")).toBeTruthy();
  });

  it("edits keys and values and emits the object", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyValuePairsField
        field={field({ id: "kvp2" })}
        value={{ existing: "v" }}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const keyInput = document.getElementById("kvp2-key-0") as HTMLInputElement;
    fireEvent.change(keyInput, { target: { value: "name" } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ name: "v" }));
  });

  it("removes a pair", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyValuePairsField
        field={field({ id: "kvp3" })}
        value={{ a: "1" }}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const removeBtn = screen.getAllByRole("button")[0];
    fireEvent.click(removeBtn);
    expect(onChange).toHaveBeenCalledWith({});
  });
});

describe("FixedKeyValuePairsField", () => {
  it("renders the empty prompt when there are no keys", () => {
    renderWithProviders(
      <FixedKeyValuePairsField
        field={field({ id: "fk", fixedKeys: [] })}
        value={{}}
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText(/Select a template/)).toBeTruthy();
  });

  it("renders inputs for static fixed keys and edits a value", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <FixedKeyValuePairsField
        field={field({ id: "fk2", fixedKeys: ["first", "last"] })}
        value={{ first: "a", last: "b" }}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const val = document.getElementById("fk2-val-first") as HTMLTextAreaElement;
    fireEvent.change(val, { target: { value: "changed" } });
    expect(onChange).toHaveBeenCalledWith(
      expect.objectContaining({ first: "changed" }),
    );
  });

  it("resolves keys from an async fixedKeys function", async () => {
    const fixedKeys = vi.fn().mockResolvedValue(["dynamicKey"]);
    renderWithProviders(
      <FixedKeyValuePairsField
        field={field({ id: "fk3", fixedKeys })}
        value={{}}
        onChange={vi.fn()}
        data={{ EmailTemplate: "x" }}
        config={cfg}
        readOnly={false}
      />,
    );
    await waitFor(() => expect(fixedKeys).toHaveBeenCalled());
    await waitFor(() =>
      expect(document.getElementById("fk3-val-dynamicKey")).toBeTruthy(),
    );
  });
});

describe("KeyTypeValueField", () => {
  it("adds a pair and changes its type to array", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyTypeValueField
        field={field({ id: "ktv" })}
        value={[]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    fireEvent.click(screen.getByText("Add Field"));
    expect(onChange).toHaveBeenCalled();
  });

  it("edits the key of an existing pair", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <KeyTypeValueField
        field={field({ id: "ktv2", keyLabel: "K" })}
        value={[{ key: "k", type: "string", value: "v" }]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const input = screen.getByDisplayValue("k");
    fireEvent.change(input, { target: { value: "k2" } });
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ key: "k2" }),
    ]);
  });
});

describe("TabWithTextField", () => {
  it("renders tabs and switches the active value", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <TabWithTextField
        field={field({
          id: "tab",
          key: "executionMode",
          label: "Mode",
          info: "pick",
          options: [
            { label: "Test", value: "0" },
            { label: "Prod", value: "1" },
          ],
          displayValue: (d: Record<string, unknown>) =>
            `url-${d.executionMode}`,
        })}
        value=""
        onChange={onChange}
        data={{ executionMode: 0 }}
        config={{ ...cfg, executionMode: 0 }}
        readOnly={false}
      />,
    );
    expect(screen.getByText("Test")).toBeTruthy();
    expect(screen.getByText("Prod")).toBeTruthy();
    // displayValue drives the read-only text field from the current mode
    const textField = document.getElementById("tab") as HTMLInputElement;
    expect(textField.value).toBe("url-0");
  });

  it("does not emit onChange for a transient tab field", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <TabWithTextField
        field={field({
          id: "tab2",
          key: "executionMode",
          options: [
            { label: "Test", value: "0" },
            { label: "Prod", value: "1" },
          ],
          transient: true,
        })}
        value=""
        onChange={onChange}
        data={{}}
        config={{ ...cfg, executionMode: 1 }}
        readOnly={false}
      />,
    );
    fireEvent.click(screen.getByText("Test").closest("button") as HTMLElement);
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("ConditionsField", () => {
  it("renders a default condition row and adds another", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ConditionsField
        field={field({ id: "cond", type: "conditions" })}
        value={[]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText("Left Operand")).toBeTruthy();
    fireEvent.click(screen.getByText(/Add Condition/));
    expect(onChange).toHaveBeenCalled();
  });

  it("edits the left operand", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ConditionsField
        field={field({ id: "cond2", type: "conditions" })}
        value={[{ left: "", operator: "equals", right: "", type: "string" }]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const left = document.getElementById("cond2-left-0") as HTMLInputElement;
    fireEvent.change(left, { target: { value: "json.a" } });
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ left: "json.a" }),
    ]);
  });

  it("removes a condition and keeps at least one row", () => {
    const onChange = vi.fn();
    renderWithProviders(
      <ConditionsField
        field={field({ id: "cond3", type: "conditions" })}
        value={[{ left: "a", operator: "equals", right: "b", type: "string" }]}
        onChange={onChange}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    const removeBtn = screen.getAllByRole("button")[0];
    fireEvent.click(removeBtn);
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ left: "", operator: "equals" }),
    ]);
  });
});

describe("SelectWithDescriptionField", () => {
  it("renders the selected option label from static options", () => {
    renderWithProviders(
      <SelectWithDescriptionField
        field={field({
          id: "swd",
          type: "select-with-description",
          options: [
            { value: "a", label: "Alpha", description: "first" },
            { value: "b", label: "Beta", description: "second" },
          ],
        })}
        value="a"
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    expect(screen.getByText("Alpha")).toBeTruthy();
  });

  it("loads async options", async () => {
    const options = vi
      .fn()
      .mockResolvedValue([{ value: "x", label: "Xray", description: "d" }]);
    renderWithProviders(
      <SelectWithDescriptionField
        field={field({ id: "swd2", type: "select-with-description", options })}
        value="x"
        onChange={vi.fn()}
        data={{}}
        config={cfg}
        readOnly={false}
      />,
    );
    await waitFor(() => expect(options).toHaveBeenCalled());
  });
});
