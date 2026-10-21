import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { SwitchRulesField } from "./switch-rules-field";
import type { SwitchRule } from "@blocks-workflow/utils/switch-handles";

const A = "switch-handle-source-aaaaa1";
const B = "switch-handle-source-bbbbb2";
const C = "switch-handle-source-ccccc3";

const rule = (handleKey: string, handleLabel: string, left = ""): SwitchRule => ({
  handleKey,
  handleLabel,
  conditionType: "all",
  conditions: left ? [{ left, operator: "equals", right: "x", type: "string" }] : [],
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

describe("SwitchRulesField", () => {
  it("H2: Add rule appends a rule with a new valid key", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, "Paid")]} onValue={onValue} />);
    fireEvent.click(screen.getByRole("button", { name: /Add rule/ }));
    const next = onValue.mock.calls[0][0] as SwitchRule[];
    expect(next).toHaveLength(2);
    expect(next[0].handleKey).toBe(A);
    expect(next[1].handleKey).toMatch(/^switch-handle-source-[a-z0-9]{6}$/);
    expect(next[1].handleKey).not.toBe(A);
  });

  it("H3/A13: renaming keeps the key; unnamed rules show Output <index>", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, ""), rule(B, "")]} onValue={onValue} />);
    expect(screen.getByText("Output 0")).toBeTruthy();
    expect(screen.getByText("Output 1")).toBeTruthy();
    fireEvent.change(screen.getAllByLabelText("Output name")[1], { target: { value: "Big" } });
    expect(onValue.mock.calls.at(-1)![0][1]).toMatchObject({ handleKey: B, handleLabel: "Big" });
  });

  it("H4/C19: reorder and delete keep each card's own conditions", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, "First", "left-a"), rule(B, "Second", "left-b"), rule(C, "Third", "left-c")]} onValue={onValue} />);
    fireEvent.click(screen.getByRole("button", { name: "Move Third up" }));
    expect((onValue.mock.calls.at(-1)![0] as SwitchRule[]).map((r) => r.handleKey)).toEqual([A, C, B]);
    const values = () => screen.getAllByRole("textbox").map((el) => (el as HTMLInputElement).value).filter((v) => v.startsWith("left-"));
    expect(values()).toEqual(["left-a", "left-c", "left-b"]);

    fireEvent.click(screen.getByRole("button", { name: "Delete First" }));
    expect((onValue.mock.calls.at(-1)![0] as SwitchRule[]).map((r) => r.handleKey)).toEqual([C, B]);
    expect(values()).toEqual(["left-c", "left-b"]);
  });

  it("H18: read-only disables every control", () => {
    renderWithProviders(<RulesHarness initial={[rule(A, "Paid", "x"), rule(B, "")]} disabled readOnly />);
    for (const button of screen.getAllByRole("button")) {
      if (/Add rule|Move|Delete/.test(button.getAttribute("aria-label") ?? button.textContent ?? "")) {
        expect((button as HTMLButtonElement).disabled).toBe(true);
      }
    }
    for (const input of screen.getAllByLabelText("Output name")) expect((input as HTMLInputElement).disabled).toBe(true);
  });

  it("C12: markup in an output name is shown as text", () => {
    const { container } = renderWithProviders(<RulesHarness initial={[rule(A, "<img src=x onerror=alert(1)>")]} />);
    expect(screen.getByText("<img src=x onerror=alert(1)>")).toBeTruthy();
    expect(container.querySelector("img")).toBeNull();
  });
});
