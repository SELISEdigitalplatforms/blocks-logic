import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import type { WorkflowStore } from "@/modules/workflow/store";
import { SwitchRulesField } from "./switch-rules-field";
import { SwitchOutputsField } from "./switch-outputs-field";
import { SwitchModeField } from "./switch-mode-field";
import type { SwitchOutput, SwitchRule } from "@blocks-workflow/utils/switch-handles";

const A = "switch-handle-source-aaaaa1";
const B = "switch-handle-source-bbbbb2";
const C = "switch-handle-source-ccccc3";

const rule = (key: string, outputName: string, left = ""): SwitchRule => ({
  key,
  outputName,
  conditionType: "all",
  conditions: left ? [{ left, operator: "equals", right: "x", type: "string" }] : [],
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const config = (store?: WorkflowStore): any => ({ tenantId: "t", workflowId: "w", nodeId: "s", store });

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
    expect(next[0].key).toBe(A);
    expect(next[1].key).toMatch(/^switch-handle-source-[a-z0-9]{6}$/);
    expect(next[1].key).not.toBe(A);
  });

  it("H3/A13: renaming keeps the key; unnamed rules show Output <index>", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, ""), rule(B, "")]} onValue={onValue} />);
    expect(screen.getByText("Output 0")).toBeTruthy();
    expect(screen.getByText("Output 1")).toBeTruthy();
    fireEvent.change(screen.getAllByLabelText("Output name")[1], { target: { value: "Big" } });
    expect(onValue.mock.calls.at(-1)![0][1]).toMatchObject({ key: B, outputName: "Big" });
  });

  it("H4/C19: reorder and delete keep each card's own conditions", () => {
    const onValue = vi.fn();
    renderWithProviders(<RulesHarness initial={[rule(A, "First", "left-a"), rule(B, "Second", "left-b"), rule(C, "Third", "left-c")]} onValue={onValue} />);
    fireEvent.click(screen.getByRole("button", { name: "Move Third up" }));
    expect((onValue.mock.calls.at(-1)![0] as SwitchRule[]).map((r) => r.key)).toEqual([A, C, B]);
    const values = () => screen.getAllByRole("textbox").map((el) => (el as HTMLInputElement).value).filter((v) => v.startsWith("left-"));
    expect(values()).toEqual(["left-a", "left-c", "left-b"]);

    fireEvent.click(screen.getByRole("button", { name: "Delete First" }));
    expect((onValue.mock.calls.at(-1)![0] as SwitchRule[]).map((r) => r.key)).toEqual([C, B]);
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

describe("SwitchOutputsField", () => {
  it("adds, renames and deletes outputs; each row shows its index", () => {
    const Harness = () => {
      const [outputs, setOutputs] = useState<SwitchOutput[]>([{ key: A, outputName: "" }]);
      return (
        <SwitchOutputsField field={{ id: "outputs", key: "outputs", type: "switch-outputs" }} value={outputs}
          onChange={setOutputs} data={{ mode: "expression", outputs }} config={config()} />
      );
    };
    renderWithProviders(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: /Add output/ }));
    expect(screen.getByText("Output 1")).toBeTruthy();
    fireEvent.change(screen.getByLabelText("Name of output 1"), { target: { value: "Late" } });
    fireEvent.click(screen.getByRole("button", { name: "Delete Output 0" }));
    expect(screen.getByLabelText("Name of output 0")).toHaveProperty("value", "Late");
  });
});

describe("SwitchModeField", () => {
  const parameters = {
    mode: "rules",
    rules: [rule(A, "")],
    outputs: [{ key: C, outputName: "" }],
    output: "",
    options: { fallbackOutput: "none", renameFallbackOutput: "", ignoreCase: false, allMatchingOutputs: false },
  };

  const renderMode = (edges: unknown[]) => {
    const onChange = vi.fn();
    let store: WorkflowStore | undefined;
    renderWithProviders(<Lazy onChange={onChange} getStore={() => store!} />, {
      seedWorkflow: (s) => {
        store = s;
        s.getState().setWorkflow({
          itemId: "w1",
          nodes: [{ id: "s", name: "Switch", category: "logic", type: "switch", version: "v2", position: { x: 0, y: 0 }, data: {}, parameters }],
          edges,
        } as never);
      },
    });
    return onChange;
  };
  const Lazy = ({ onChange, getStore }: { onChange: (v: string) => void; getStore: () => WorkflowStore }) => (
    <SwitchModeField field={{ id: "mode", key: "mode", type: "switch-mode" }} value="rules" onChange={onChange} data={parameters} config={config(getStore())} />
  );
  const choose = async (label: string) => {
    fireEvent.click(screen.getByRole("combobox"));
    await act(async () => {
      fireEvent.click(await screen.findByRole("option", { name: label }));
    });
  };

  it("switches without asking when no connection would be removed", async () => {
    const onChange = renderMode([]);
    await choose("Expression");
    expect(onChange).toHaveBeenCalledWith("expression");
    expect(screen.queryByText("Switch mode?")).toBeNull();
  });

  it("C2: asks when connections would be removed; Cancel keeps the mode, Remove switches", async () => {
    const onChange = renderMode([{ id: "e1", source: "s", target: "x", sourceHandle: A, targetHandle: "target" }]);
    await choose("Expression");
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/1 connection is on an output/)).toBeTruthy();
    await waitFor(() => expect(document.activeElement?.textContent).toBe("Cancel"));
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    expect(onChange).not.toHaveBeenCalled();

    await choose("Expression");
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Remove" }));
    expect(onChange).toHaveBeenCalledWith("expression");
  });
});
