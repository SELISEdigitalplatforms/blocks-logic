import React, { useState } from "react";
import { act, fireEvent, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { FormBuilder } from "../form-builder";
import { NodeSchemaLogicFilterV2 } from "../../../node-schemas/node-schema-logic-filter-v2";
import { FilterConditionsField } from "./filter-conditions-field";
import { SwitchRulesField } from "./switch-rules-field";
import type { Condition } from "./conditions-field";
import type { SwitchRule } from "@blocks-workflow/utils/switch-handles";

const cond = (left = "", operator = "equals", right = "", type: Condition["type"] = "string"): Condition => ({ left, operator, right, type });

/** The Filter inspector's Parameters tab: the real schema fields through the real form builder. */
const FilterForm = ({ initial, onSave }: { initial: Record<string, unknown>; onSave?: (data: Record<string, unknown>) => void }) => {
  const [data, setData] = useState(initial);
  return (
    <FormBuilder
      fields={NodeSchemaLogicFilterV2.schema.parameters}
      data={data}
      onChange={(next) => {
        onSave?.(next);
        setData(next);
      }}
    />
  );
};

const renderFilter = (initial: Record<string, unknown>) => {
  const onSave = vi.fn();
  renderWithProviders(<FilterForm initial={initial} onSave={onSave} />);
  return { lastSaved: () => onSave.mock.calls.at(-1)![0] as Record<string, unknown>, onSave };
};

const newNodeParameters = () => NodeSchemaLogicFilterV2.transform!({ id: "f", parameters: {} } as never).parameters as Record<string, unknown>;

const toggle = (name: "AND" | "OR") => within(screen.getByRole("group", { name: "Match" })).getByRole("button", { name });

const openPicker = async (index: number) => {
  const trigger = screen.getByRole("button", { name: `Condition ${index} type and operator` });
  await act(async () => {
    fireEvent.pointerDown(trigger, { button: 0, ctrlKey: false, pointerType: "mouse" });
  });
  return screen.findByRole("menu");
};

const pick = async (index: number, type: string, operator: string) => {
  const menu = await openPicker(index);
  await act(async () => {
    fireEvent.keyDown(within(menu).getByRole("menuitem", { name: type }), { key: "ArrowRight" });
  });
  const item = await screen.findByRole("menuitem", { name: operator });
  await act(async () => {
    fireEvent.click(item);
  });
};

describe("FilterConditionsField (SPEC9)", () => {
  it("H3/H4: a new node shows the condition list alone: AND, one blank String equals condition, Conditions once", () => {
    renderFilter(newNodeParameters());

    expect(screen.getAllByText("Conditions")).toHaveLength(1);
    expect(toggle("AND").getAttribute("aria-pressed")).toBe("true");
    expect(toggle("OR").getAttribute("aria-pressed")).toBe("false");
    expect((screen.getByLabelText("Condition 1 value to check") as HTMLInputElement).value).toBe("");
    expect((screen.getByLabelText("Condition 1 value to compare with") as HTMLInputElement).value).toBe("");
    expect(screen.getByRole("button", { name: "Condition 1 type and operator" }).textContent).toBe("Stringequals");
    expect(screen.queryByLabelText("Condition 2 value to check")).toBeNull();
    expect(screen.queryByRole("button", { name: /Remove condition/ })).toBeNull();
    expect(screen.getByRole("button", { name: /Add condition/ })).toBeTruthy();
    // No Switch rule card around it.
    expect(screen.queryByText(/Routing Rule/)).toBeNull();
    expect(screen.queryByRole("switch", { name: "Rename Output" })).toBeNull();
    expect(screen.queryByLabelText("Output Name")).toBeNull();
    expect(screen.queryByText(/Fallback/)).toBeNull();
  });

  it("H5: OR saves conditionType 'or' and AND saves 'and', with no filterConditions key", () => {
    const { lastSaved } = renderFilter(newNodeParameters());

    fireEvent.click(toggle("OR"));
    expect(lastSaved()).toEqual({ conditionType: "or", conditions: [cond()] });
    expect(toggle("OR").getAttribute("aria-pressed")).toBe("true");

    fireEvent.click(toggle("AND"));
    expect(lastSaved()).toEqual({ conditionType: "and", conditions: [cond()] });
    expect(lastSaved()).not.toHaveProperty("filterConditions");
    expect(toggle("AND").getAttribute("aria-pressed")).toBe("true");
  });

  it("H5: adding, editing and removing conditions saves the top-level conditions and keeps the condition type", () => {
    const { lastSaved } = renderFilter({ conditionType: "or", conditions: [cond("a", "equals", "x")] });

    fireEvent.click(screen.getByRole("button", { name: /Add condition/ }));
    expect(lastSaved()).toEqual({ conditionType: "or", conditions: [cond("a", "equals", "x"), cond()] });

    fireEvent.change(screen.getByLabelText("Condition 2 value to check"), { target: { value: "{{ $json.status }}" } });
    fireEvent.change(screen.getByLabelText("Condition 2 value to compare with"), { target: { value: "paid" } });
    expect(lastSaved()).toEqual({ conditionType: "or", conditions: [cond("a", "equals", "x"), cond("{{ $json.status }}", "equals", "paid")] });

    fireEvent.click(screen.getByRole("button", { name: "Remove condition 1" }));
    expect(lastSaved()).toEqual({ conditionType: "or", conditions: [cond("{{ $json.status }}", "equals", "paid")] });
    expect(Object.keys(lastSaved()).sort()).toEqual(["conditionType", "conditions"]);
  });

  it("H5: the type and operator picker sets both; a unary operator hides the compare value and clears it", async () => {
    const { lastSaved } = renderFilter({ conditionType: "and", conditions: [cond("{{ $json.total }}", "equals", "5")] });

    await pick(1, "Number", "Greater than");
    expect(lastSaved().conditions).toEqual([cond("{{ $json.total }}", "greater_than", "5", "number")]);

    await pick(1, "Boolean", "Is true");
    expect(lastSaved().conditions).toEqual([{ left: "{{ $json.total }}", operator: "is_true", right: undefined, type: "boolean" }]);
    expect(screen.queryByLabelText("Condition 1 value to compare with")).toBeNull();
  });

  it("C6: a number literal that cannot be read shows the existing hint", () => {
    renderFilter({ conditionType: "and", conditions: [cond("{{ $json.total }}", "greater_than", "abc", "number")] });

    expect(screen.getByRole("status").textContent).toBe("Not a number, so this condition never matches.");
  });

  it("H11: a saved Filter reopens with its conditions, operator labels and OR", () => {
    renderFilter({
      conditionType: "or",
      conditions: [cond("{{ $json.status }}", "equals", "paid"), cond("{{ $json.total }}", "greater_than", "100", "number")],
    });

    expect(toggle("OR").getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "Condition 1 type and operator" }).textContent).toBe("Stringequals");
    expect(screen.getByRole("button", { name: "Condition 2 type and operator" }).textContent).toBe("Numbergreater than");
    expect((screen.getByLabelText("Condition 2 value to check") as HTMLInputElement).value).toBe("{{ $json.total }}");
    expect((screen.getByLabelText("Condition 2 value to compare with") as HTMLInputElement).value).toBe("100");
  });

  it("H5: a saved 'and', or no condition type, shows AND", () => {
    const { unmount } = renderWithProviders(<FilterForm initial={{ conditionType: "and", conditions: [cond()] }} />);
    expect(toggle("AND").getAttribute("aria-pressed")).toBe("true");
    unmount();

    renderWithProviders(<FilterForm initial={{ conditions: [] }} />);
    expect(toggle("AND").getAttribute("aria-pressed")).toBe("true");
    expect(screen.queryByLabelText("Condition 1 value to check")).toBeNull();
  });

  it("editing a condition keeps the saved condition type as it is", () => {
    const { lastSaved } = renderFilter({ conditionType: 1, conditions: [cond()] });

    fireEvent.change(screen.getByLabelText("Condition 1 value to check"), { target: { value: "x" } });
    expect(lastSaved()).toStrictEqual({ conditionType: 1, conditions: [cond("x")] });
  });

  it("the variable picker names the Conditions heading, not the internal key", () => {
    renderFilter(newNodeParameters());

    expect(screen.getAllByRole("button", { name: /^Insert a configuration variable into Conditions condition 1 left operand/ })).toHaveLength(1);
    expect(screen.queryByRole("button", { name: /filterConditions/ })).toBeNull();
  });

  it.each([
    ["read-only", { readOnly: true }],
    ["disabled", { disabled: true }],
  ])("C12: %s disables the toggle, picker, inputs, add and remove", (_, { readOnly, disabled }) => {
    const onChange = vi.fn();
    renderWithProviders(
      <FilterConditionsField
        field={{ id: "filterConditions", key: "filterConditions", type: "filter-conditions", transient: true, disabled }}
        value={undefined as never}
        onChange={onChange}
        readOnly={readOnly}
        data={{ conditionType: "and", conditions: [cond("a", "equals", "x"), cond("b", "equals", "y")] }}
        config={{ tenantId: "t", workflowId: "w", nodeId: "f" } as never}
      />,
    );

    for (const name of ["AND", "OR"] as const) expect((toggle(name) as HTMLButtonElement).disabled).toBe(true);
    for (const name of [/Add condition/, "Remove condition 1", "Remove condition 2", "Condition 1 type and operator", "Condition 2 type and operator"]) {
      expect((screen.getByRole("button", { name }) as HTMLButtonElement).disabled).toBe(true);
    }
    for (const label of ["Condition 1 value to check", "Condition 1 value to compare with", "Condition 2 value to check", "Condition 2 value to compare with"]) {
      expect((screen.getByLabelText(label) as HTMLInputElement).disabled).toBe(true);
    }
  });
});

describe("Switch rules are unchanged by Filter (SPEC9 C17)", () => {
  const RulesHarness = ({ onValue }: { onValue: (rules: SwitchRule[]) => void }) => {
    const [rules, setRules] = React.useState<SwitchRule[]>([
      { handleKey: "switch-handle-source-aaaaa1", handleLabel: "", conditionType: "all", conditions: [cond("a", "equals", "x")] },
    ]);
    return (
      <SwitchRulesField
        field={{ id: "rules", key: "rules", type: "switch-rules" }}
        value={rules}
        onChange={(next) => {
          setRules(next);
          onValue(next);
        }}
        data={{ mode: "rules", rules }}
        config={{ tenantId: "t", workflowId: "w", nodeId: "s" } as never}
      />
    );
  };

  it("a Switch rule's toggle still saves 'or' and 'all', never 'and'", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness onValue={onValue} />);

    fireEvent.click(toggle("OR"));
    expect((onValue.mock.calls.at(-1)![0] as SwitchRule[])[0].conditionType).toBe("or");
    fireEvent.click(toggle("AND"));
    expect((onValue.mock.calls.at(-1)![0] as SwitchRule[])[0].conditionType).toBe("all");
  });
});
