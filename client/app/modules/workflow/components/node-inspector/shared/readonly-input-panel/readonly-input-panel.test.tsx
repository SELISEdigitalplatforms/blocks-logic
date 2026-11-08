import { act, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { NodeExecutionStatus } from "@blocks-workflow/utils/workflow-execution-editor.util";
import { ReadonlyInputPanel } from "./readonly-input-panel";

const clipboard = vi.hoisted(() => ({
  copyToClipboard: vi.fn().mockResolvedValue(true),
}));
vi.mock("@blocks-workflow/utils/copy-to-clipboard", () => clipboard);

// Wraps the real schema inference so the memo budget (spec §5.2) can count its calls.
const inference = vi.hoisted(() => ({ calls: 0 }));
vi.mock("@blocks-workflow/utils/runtime-node-data", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@blocks-workflow/utils/runtime-node-data")>();
  return {
    ...actual,
    inferSchemaFromRuntimeRows: (...args: Parameters<typeof actual.inferSchemaFromRuntimeRows>) => {
      inference.calls += 1;
      return actual.inferSchemaFromRuntimeRows(...args);
    },
  };
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (id = "n1", extra: Record<string, unknown> = {}): any => ({
  id,
  name: id,
  type: "action",
  position: { x: 0, y: 0 },
  parameters: {},
  ...extra,
});

const run = (extra: Record<string, unknown>) => ({
  id: `ne-${String(extra.runIndex ?? 0)}`,
  nodeId: "n1",
  runIndex: 0,
  status: NodeExecutionStatus.Completed,
  output: [],
  error: null,
  ...extra,
});

type Seed = {
  editorMode?: string;
  executedNodes?: unknown[];
  nodeExtra?: Record<string, unknown>;
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const seed = ({ editorMode = "execution", executedNodes = [], nodeExtra = {} }: Seed) => (store: any) => {
  const n = node("n1", nodeExtra);
  store.getState().addNode(n);
  store.setState({ selectedNode: n, editorMode, executedNodes });
};

/** The panel with LayoutWithIO's own collapse state, so the collapse button works as in the inspector. */
function Collapsible() {
  const [collapsed, setCollapsed] = useState(false);
  return <ReadonlyInputPanel isCollapsed={collapsed} onToggleCollapse={() => setCollapsed((c) => !c)} />;
}

beforeEach(() => {
  vi.clearAllMocks();
  inference.calls = 0;
});

describe("ReadonlyInputPanel", () => {
  it("renders nothing without a selected node (C4)", () => {
    const { container } = renderWithProviders(<ReadonlyInputPanel />);
    expect(container.textContent).toBe("");
  });

  it("shows the node's own input as item cards on the Schema tab (H5, example 1)", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ name: "Ada", age: 36 }] })] }),
    });
    expect(screen.getByRole("heading", { name: "Input" })).toBeTruthy();
    expect(screen.getByText("item 1:")).toBeTruthy();
    expect(screen.getByText("name:")).toBeTruthy();
    expect(screen.getByText("Ada")).toBeTruthy();
    expect(screen.getByText("age:")).toBeTruthy();
    expect(screen.getByText("36")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Copy value of name" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Copy value of age" })).toBeTruthy();
  });

  it("copies a field's value with its copy button (H5)", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ name: "Ada", age: 36 }] })] }),
    });
    await user.click(screen.getByRole("button", { name: "Copy value of name" }));
    expect(clipboard.copyToClipboard).toHaveBeenCalledWith("Ada");
    await user.click(screen.getByRole("button", { name: "Copy value of age" }));
    expect(clipboard.copyToClipboard).toHaveBeenLastCalledWith("36");
  });

  it("shows one column per key across all rows on the Table tab (H6)", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ name: "Ada" }, { age: 36 }] })] }),
    });
    await user.click(screen.getByRole("tab", { name: "Table" }));
    const headers = screen.getAllByRole("columnheader").map((th) => th.textContent);
    expect(headers).toEqual(["name", "age"]);
    const cells = screen.getAllByRole("row").slice(1).map((tr) => within(tr).getAllByRole("cell").map((td) => td.textContent));
    expect(cells).toEqual([
      ["Ada", "undefined"],
      ["undefined", "36"],
    ]);
  });

  it("pretty-prints the rows on the JSON tab and copies exactly that text (H7, example 1)", async () => {
    const user = userEvent.setup();
    const { container } = renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ name: "Ada", age: 36 }] })] }),
    });
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    const json = '[\n  {\n    "name": "Ada",\n    "age": 36\n  }\n]';
    expect(container.querySelector("pre code")?.textContent).toBe(json);
    await user.click(screen.getByRole("button", { name: "Copy input JSON" }));
    expect(clipboard.copyToClipboard).toHaveBeenCalledWith(json);
  });

  it("shows only the run with the highest runIndex (H4, example 2)", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({
        executedNodes: [run({ runIndex: 1, input: [{ a: 2 }] }), run({ runIndex: 0, input: [{ a: 1 }] })],
      }),
    });
    expect(screen.getByText("2")).toBeTruthy();
    expect(screen.queryByText("1")).toBeNull();
  });

  it("takes the later entry when two runs share the highest runIndex (D4)", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({
        executedNodes: [run({ runIndex: 1, input: [{ a: "first" }] }), run({ runIndex: 1, input: [{ a: "second" }] })],
      }),
    });
    expect(screen.getByText("second")).toBeTruthy();
    expect(screen.queryByText("first")).toBeNull();
  });

  it("ignores other nodes' runs (example 3)", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ editorMode: "version", executedNodes: [run({ nodeId: "other", input: [{ x: 1 }] })] }),
    });
    expect(screen.getByText("No runtime input schema available.")).toBeTruthy();
    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getByText("No runtime input data available.")).toBeTruthy();
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    expect(screen.getByText("No runtime input data available.")).toBeTruthy();
  });

  it.each([
    ["no run", []],
    ["an empty input", [run({ input: [] })]],
    ["a null input (example 6)", [run({ input: null })]],
    ["an input that is not an array", [run({ input: { name: "Ada" } })]],
  ])("shows the empty texts on every tab for %s (C3)", async (_label, executedNodes) => {
    const user = userEvent.setup();
    const { container } = renderWithProviders(<ReadonlyInputPanel />, { seedWorkflow: seed({ executedNodes }) });
    expect(screen.getByText("No runtime input schema available.")).toBeTruthy();
    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getByText("No runtime input data available.")).toBeTruthy();
    expect(container.querySelector("table")).toBeNull();
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    const empty = screen.getByText("No runtime input data available.");
    expect(empty.className).toContain("p-3");
    expect(container.querySelector("pre")).toBeNull();
  });

  it("shows primitive rows as cards and in the (value) column (example 4)", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: ["a", 5] })] }),
    });
    expect(screen.getByText("item 1:")).toBeTruthy();
    expect(screen.getByText("item 2:")).toBeTruthy();
    expect(screen.getByText("a")).toBeTruthy();
    expect(screen.getByText("5")).toBeTruthy();

    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getAllByRole("columnheader").map((th) => th.textContent)).toEqual(["(value)"]);
    expect(screen.getAllByRole("cell").map((td) => td.textContent)).toEqual(["a", "5"]);
  });

  it("adds the (value) column next to object columns when rows are mixed (H6)", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: ["a", { name: "Ada" }] })] }),
    });
    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getAllByRole("columnheader").map((th) => th.textContent)).toEqual(["(value)", "name"]);
    expect(screen.getAllByRole("cell").map((td) => td.textContent)).toEqual(["a", "", "", "Ada"]);
  });

  it("shows nested values as single-line JSON and copies the same text (C8)", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ tags: ["x"], address: { city: "Paris" } }] })] }),
    });
    expect(screen.getByText('["x"]')).toBeTruthy();
    expect(screen.getByText('{"city":"Paris"}')).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Copy value of tags" }));
    expect(clipboard.copyToClipboard).toHaveBeenCalledWith('["x"]');
    await user.click(screen.getByRole("button", { name: "Copy value of address" }));
    expect(clipboard.copyToClipboard).toHaveBeenLastCalledWith('{"city":"Paris"}');

    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getAllByRole("cell").map((td) => td.textContent)).toEqual(['["x"]', '{"city":"Paris"}']);
  });

  it("collapses and expands the tab body, switching the button's label (H8)", async () => {
    const user = userEvent.setup();
    renderWithProviders(<Collapsible />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ name: "Ada" }] })] }),
    });
    expect(screen.getByText("name:")).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Collapse input" }));
    expect(screen.queryByText("name:")).toBeNull();
    expect(screen.getByRole("button", { name: "Expand input" })).toBeTruthy();

    await user.click(screen.getByRole("button", { name: "Expand input" }));
    expect(screen.getByText("name:")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Collapse input" })).toBeTruthy();
  });

  it("has no collapse button without onToggleCollapse", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ name: "Ada" }] })] }),
    });
    expect(screen.queryByRole("button", { name: "Collapse input" })).toBeNull();
  });

  it("has nothing draggable on any tab (C1)", async () => {
    const user = userEvent.setup();
    const { container } = renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({ executedNodes: [run({ input: [{ name: "Ada", tags: ["x"] }, "a"] })] }),
    });
    expect(container.querySelector('[draggable="true"]')).toBeNull();
    expect(screen.queryByTitle(/Drag to use/)).toBeNull();
    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(container.querySelector('[draggable="true"]')).toBeNull();
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    expect(container.querySelector('[draggable="true"]')).toBeNull();
  });

  it("shows no mock-data controls, even in the editor with pinned data (C2)", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({
        editorMode: "editor",
        nodeExtra: { pinData: [{ pinned: true }] },
        executedNodes: [run({ input: [{ name: "Ada" }] })],
      }),
    });
    for (const text of ["Set mock data", "Edit mock data", "Remove mock data", "Mock data"]) {
      expect(screen.queryByText(text)).toBeNull();
      expect(screen.queryByRole("button", { name: text })).toBeNull();
    }
    expect(document.querySelector("textarea")).toBeNull();
    // pinData does not affect the input (§6)
    expect(screen.getByText("Ada")).toBeTruthy();
    expect(screen.queryByText("pinned:")).toBeNull();
  });

  it("still shows a failed run's input, with no error alert (C5)", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({
        executedNodes: [run({ status: NodeExecutionStatus.Failed, error: "boom", input: [{ name: "Ada" }] })],
      }),
    });
    expect(screen.getByText("Ada")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByText("boom")).toBeNull();
  });

  it("has no predecessor selector, branch headers or run summaries (C6)", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed({
        nodeExtra: { type: "splitOut", parameters: { fields: "missing" } },
        executedNodes: [run({ input: [{ name: "Ada" }], inputItemCount: 1, outputItemCount: 0 })],
      }),
    });
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.queryByText(/in → .* out/)).toBeNull();
    expect(screen.queryByText(/wasn't found in any input item/)).toBeNull();
    expect(screen.queryByRole("button", { name: /Copy branch key of/ })).toBeNull();
  });

  it("never writes to the workflow store, whatever the user does (C7)", async () => {
    const user = userEvent.setup();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<Collapsible />, {
      seedWorkflow: (s) => {
        store = s;
        seed({ executedNodes: [run({ input: [{ name: "Ada" }] })] })(s);
      },
    });
    const writes = vi.fn();
    const unsubscribe = store.subscribe(writes);

    await user.click(screen.getByRole("button", { name: "Copy value of name" }));
    await user.click(screen.getByRole("tab", { name: "Table" }));
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    await user.click(screen.getByRole("button", { name: "Copy input JSON" }));
    await user.click(screen.getByRole("button", { name: "Collapse input" }));
    await user.click(screen.getByRole("button", { name: "Expand input" }));

    unsubscribe();
    expect(writes).not.toHaveBeenCalled();
  });

  it("recomputes the rows only when executedNodes or the selected node change (§5.2, D5)", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: (s) => {
        store = s;
        seed({ executedNodes: [run({ input: [{ name: "Ada" }] })] })(s);
      },
    });
    const afterMount = inference.calls;
    expect(afterMount).toBeGreaterThan(0);

    act(() => {
      store.getState().addNode(node("n2"));
      store.setState({ edgesMap: {}, executedItems: [] });
    });
    expect(inference.calls).toBe(afterMount);

    act(() => {
      store.setState({ executedNodes: [run({ input: [{ name: "Grace" }] })] });
    });
    expect(inference.calls).toBe(afterMount + 1);
    expect(screen.getByText("Grace")).toBeTruthy();
  });

  it("shows the newly selected node's input when the selection changes (H12)", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: (s) => {
        store = s;
        seed({
          executedNodes: [run({ input: [{ name: "Ada" }] }), run({ id: "ne-n2", nodeId: "n2", input: [{ name: "Grace" }] })],
        })(s);
        s.getState().addNode(node("n2"));
      },
    });
    expect(screen.getByText("Ada")).toBeTruthy();

    act(() => {
      store.setState({ selectedNode: store.getState().nodesMap.n2 });
    });
    expect(screen.getByText("Grace")).toBeTruthy();
    expect(screen.queryByText("Ada")).toBeNull();
  });
});
