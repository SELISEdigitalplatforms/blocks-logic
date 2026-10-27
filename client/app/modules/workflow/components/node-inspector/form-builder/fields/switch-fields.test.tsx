import { act, fireEvent, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SwitchRulesField } from "./switch-rules-field";
import { SWITCH_CONDITION_TYPES, comparePlaceholder, conditionsSummary, literalProblem } from "./switch-condition-list";
import { TYPE_OPERATORS, type Condition } from "./conditions-field";
import type { SwitchRule } from "@blocks-workflow/utils/switch-handles";

const A = "switch-handle-source-aaaaa1";
const B = "switch-handle-source-bbbbb2";
const C = "switch-handle-source-ccccc3";

const cond = (left = "", operator = "equals", right = "", type: Condition["type"] = "string"): Condition => ({ left, operator, right, type });

const rule = (handleKey: string, handleLabel: string, left = "", conditionType = "all"): SwitchRule => ({
  handleKey,
  handleLabel,
  conditionType,
  conditions: [cond(left, "equals", "x")],
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const config = (): any => ({ tenantId: "t", workflowId: "w", nodeId: "s" });

const RulesHarness = ({ initial, readOnly, disabled, onValue }: { initial: SwitchRule[]; readOnly?: boolean; disabled?: boolean; onValue?: (v: SwitchRule[]) => void }) => {
  const [rules, setRules] = useState(initial);
  return (
    <SwitchRulesField
      field={{ id: "rules", key: "rules", type: "switch-rules", disabled }}
      value={rules}
      onChange={(v) => {
        setRules(v);
        onValue?.(v);
      }}
      readOnly={readOnly}
      data={{ mode: "rules", rules }}
      config={config()}
    />
  );
};

const last = (fn: ReturnType<typeof vi.fn>) => fn.mock.calls.at(-1)![0] as SwitchRule[];
const header = (n: number) =>
  screen.getByRole("button", { name: (accessibleName) => accessibleName.startsWith(`Routing Rule ${n},`) });

describe("SwitchRulesField", () => {
  it("opens a single rule, and starts several rules closed", () => {
    const { unmount } = renderWithProviders(<RulesHarness initial={[rule(A, "")]} />);
    expect(header(1).getAttribute("aria-expanded")).toBe("true");
    unmount();

    renderWithProviders(<RulesHarness initial={[rule(A, ""), rule(B, "")]} />);
    expect(header(1).getAttribute("aria-expanded")).toBe("false");
    expect(header(2).getAttribute("aria-expanded")).toBe("false");
    fireEvent.click(header(2));
    expect(header(2).getAttribute("aria-expanded")).toBe("true");
    fireEvent.click(header(2));
    expect(header(2).getAttribute("aria-expanded")).toBe("false");
  });

  it("collapsed rules show a summary of their first condition and their output number or name", () => {
    renderWithProviders(<RulesHarness initial={[rule(A, "", "{{ $json.a }}"), rule(B, "Paid", "{{ $json.b }}")]} />);
    expect(screen.getByText("{{ $json.a }} equals x")).toBeTruthy();
    expect(screen.getByText("0")).toBeTruthy();
    expect(screen.getByText("Paid")).toBeTruthy();
  });

  it("Add Routing Rule appends an open rule with a new valid key and one empty condition", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, ""), rule(B, "")]} onValue={onValue} />);
    fireEvent.click(screen.getByRole("button", { name: /Add Routing Rule/ }));
    const next = last(onValue);
    expect(next).toHaveLength(3);
    expect(next[2].handleKey).toMatch(/^switch-handle-source-[a-z0-9]{6}$/);
    expect([A, B]).not.toContain(next[2].handleKey);
    expect(next[2].conditions).toEqual([cond()]);
    expect(header(3).getAttribute("aria-expanded")).toBe("true");
  });

  it("has no reorder controls; delete removes only that rule and keeps each card's conditions", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, "", "left-a"), rule(B, "", "left-b"), rule(C, "", "left-c")]} onValue={onValue} />);
    expect(screen.queryByRole("button", { name: /Move/ })).toBeNull();
    fireEvent.click(header(3));
    fireEvent.click(screen.getByRole("button", { name: "Delete Routing Rule 1" }));
    expect(last(onValue).map((r) => r.handleKey)).toEqual([B, C]);
    // The open card (was rule 3, now rule 2) still shows its own condition.
    expect(header(2).getAttribute("aria-expanded")).toBe("true");
    expect(screen.getByDisplayValue("left-c")).toBeTruthy();
  });

  it("Rename Output: off for an unnamed rule; on shows the name field; off again clears the name", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, "")]} onValue={onValue} />);
    const toggle = screen.getByRole("switch", { name: "Rename Output" });
    expect(toggle.getAttribute("aria-checked")).toBe("false");
    expect(screen.queryByLabelText("Output Name")).toBeNull();

    fireEvent.click(toggle);
    fireEvent.change(screen.getByLabelText("Output Name"), { target: { value: "Paid" } });
    expect(last(onValue)[0]).toMatchObject({ handleKey: A, handleLabel: "Paid" });

    fireEvent.click(screen.getByRole("switch", { name: "Rename Output" }));
    expect(last(onValue)[0].handleLabel).toBe("");
    expect(screen.queryByLabelText("Output Name")).toBeNull();
  });

  it("a named rule opens with Rename Output on", () => {
    renderWithProviders(<RulesHarness initial={[rule(A, "Paid")]} />);
    expect(screen.getByRole("switch", { name: "Rename Output" }).getAttribute("aria-checked")).toBe("true");
    expect(screen.getByLabelText("Output Name")).toHaveProperty("value", "Paid");
  });

  it("AND / OR sets the rule's condition type, and labels the gap between conditions", () => {
    const onValue = vi.fn();
    const twoConditions: SwitchRule = { ...rule(A, ""), conditions: [cond("a"), cond("b")] };
    renderWithProviders(<RulesHarness initial={[twoConditions]} onValue={onValue} />);
    const match = screen.getByRole("group", { name: "Match" });
    expect(within(match).getByRole("button", { name: "AND" }).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(within(match).getByRole("button", { name: "OR" }));
    expect(last(onValue)[0].conditionType).toBe("or");
    expect(within(match).getByRole("button", { name: "OR" }).getAttribute("aria-pressed")).toBe("true");
  });

  it("adds and removes conditions", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, "", "a")]} onValue={onValue} />);
    expect(screen.queryByRole("button", { name: /Remove condition/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /Add condition/ }));
    expect(last(onValue)[0].conditions).toHaveLength(2);
    fireEvent.click(screen.getByRole("button", { name: "Remove condition 1" }));
    expect(last(onValue)[0].conditions).toEqual([cond()]);
  });

  it("the type and operator picker offers every supported type and sets both together", async () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, "", "{{ $json.n }}")]} onValue={onValue} />);
    const trigger = screen.getByRole("button", { name: "Condition 1 type and operator" });
    expect(trigger.textContent).toContain("String");
    await act(async () => {
      fireEvent.pointerDown(trigger, { button: 0, ctrlKey: false, pointerType: "mouse" });
    });
    const menu = await screen.findByRole("menu");
    expect(within(menu).getAllByRole("menuitem").map((el) => el.textContent)).toEqual(SWITCH_CONDITION_TYPES.map((t) => t.label));

    await act(async () => {
      fireEvent.keyDown(within(menu).getByRole("menuitem", { name: "Number" }), { key: "ArrowRight" });
    });
    const greater = await screen.findByRole("menuitem", { name: "Greater than" });
    await act(async () => {
      fireEvent.click(greater);
    });
    expect(last(onValue)[0].conditions[0]).toMatchObject({ type: "number", operator: "greater_than" });
  });

  it("Is true hides the compare value and clears it", () => {
    const onValue = vi.fn();
    const boolRule: SwitchRule = { ...rule(A, ""), conditions: [cond("{{ $json.ok }}", "is_true", "", "boolean")] };
    renderWithProviders(<RulesHarness initial={[boolRule]} onValue={onValue} />);
    expect(screen.queryByLabelText("Condition 1 value to compare with")).toBeNull();
  });

  it("shows a hint for a typed value the server cannot read as the chosen type", () => {
    const numberRule: SwitchRule = { ...rule(A, ""), conditions: [cond("{{ $json.n }}", "greater_than", "abc", "number")] };
    renderWithProviders(<RulesHarness initial={[numberRule]} />);
    expect(screen.getByRole("status").textContent).toBe("Not a number, so this condition never matches.");
  });

  it("read-only disables every control", () => {
    renderWithProviders(<RulesHarness initial={[rule(A, "Paid", "x")]} disabled readOnly />);
    for (const name of [/Add Routing Rule/, /Delete Routing Rule 1/, /Add condition/, /type and operator/]) {
      expect((screen.getByRole("button", { name }) as HTMLButtonElement).disabled).toBe(true);
    }
    expect((screen.getByRole("switch", { name: "Rename Output" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByLabelText("Output Name") as HTMLInputElement).disabled).toBe(true);
  });

  it("markup in an output name is shown as text", () => {
    const { container } = renderWithProviders(<RulesHarness initial={[rule(A, "<img src=x onerror=alert(1)>")]} />);
    expect(screen.getAllByText("<img src=x onerror=alert(1)>").length).toBeGreaterThan(0);
    expect(container.querySelector("img")).toBeNull();
  });
});

describe("Switch condition helpers", () => {
  it("offers only the types the server evaluates, each with its operator list", () => {
    expect(SWITCH_CONDITION_TYPES.map((t) => t.value).sort()).toEqual(["array", "boolean", "date_time", "number", "string"]);
    for (const type of SWITCH_CONDITION_TYPES) expect(TYPE_OPERATORS[type.value]?.length).toBeGreaterThan(0);
  });

  it("summarises the first condition and counts the rest", () => {
    expect(conditionsSummary([], "all")).toBe("No conditions yet");
    expect(conditionsSummary([cond("{{ $json.a }}", "equals", "x"), cond("b")], "or")).toBe("{{ $json.a }} equals x or 1 more");
    expect(conditionsSummary([cond("{{ $json.ok }}", "is_true", "", "boolean")], "all")).toBe("{{ $json.ok }} is true");
  });

  it("placeholders follow the type and list operators", () => {
    expect(comparePlaceholder(cond("", "greater_than", "", "number"))).toBe("e.g. 100");
    expect(comparePlaceholder(cond("", "in"))).toContain("a, b, c");
  });

  it("flags only literals that are certainly unreadable; expressions are never flagged", () => {
    expect(literalProblem(cond("", "equals", "1,000", "number"), "right")).toBe("");
    expect(literalProblem(cond("", "equals", "abc", "number"), "right")).toContain("Not a number");
    expect(literalProblem(cond("", "equals", "{{ $json.x }}", "number"), "right")).toBe("");
    expect(literalProblem(cond("", "equals", "yes", "boolean"), "right")).toContain("Not true or false");
    expect(literalProblem(cond("", "equals", "TRUE", "boolean"), "right")).toBe("");
    expect(literalProblem(cond("", "equals", "soon", "date_time"), "right")).toContain("Not a date");
    expect(literalProblem(cond("", "equals", "anything"), "right")).toBe("");
  });
});
