import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { NodeExecutionStatus } from "@blocks-workflow/utils/workflow-execution-editor.util";
import { ReadonlyInputPanel } from "./readonly-input-panel";

const clipboard = vi.hoisted(() => ({
  copyToClipboard: vi.fn().mockResolvedValue(true),
}));
vi.mock("@blocks-workflow/utils/copy-to-clipboard", () => clipboard);

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (extra: Record<string, unknown> = {}): any => ({
  id: "n1",
  name: "n1",
  type: "action",
  category: "action",
  version: "v1",
  position: { x: 0, y: 0 },
  parameters: {},
  ...extra,
});

const seed =
  (
    executedNodes: unknown[],
    { editorMode = "execution", nodeExtra = {} }: { editorMode?: string; nodeExtra?: Record<string, unknown> } = {},
  ) =>
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (store: any) => {
    const n = node(nodeExtra);
    store.getState().addNode(n);
    store.setState({ selectedNode: n, editorMode, executedNodes });
  };

const run = (input: unknown, extra: Record<string, unknown> = {}) => ({
  nodeId: "n1",
  runIndex: 0,
  status: NodeExecutionStatus.Completed,
  input,
  output: [],
  ...extra,
});

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ReadonlyInputPanel", () => {
  it("renders nothing without a selected node", () => {
    const { container } = renderWithProviders(<ReadonlyInputPanel />);
    expect(container.textContent).toBe("");
  });

  it("shows object rows on the Schema, Table and JSON tabs", async () => {
    const user = userEvent.setup();
    const rows = [{ name: "Ada", age: 36 }];
    const { container } = renderWithProviders(<ReadonlyInputPanel />, { seedWorkflow: seed([run(rows)]) });

    expect(screen.getByText("Input")).toBeTruthy();
    expect(screen.getByText("item 1:")).toBeTruthy();
    expect(screen.getByText("name:")).toBeTruthy();
    expect(screen.getByText("Ada")).toBeTruthy();
    expect(screen.getByText("age:")).toBeTruthy();
    expect(screen.getByText("36")).toBeTruthy();

    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getByRole("columnheader", { name: "name" })).toBeTruthy();
    expect(screen.getByRole("columnheader", { name: "age" })).toBeTruthy();
    expect(screen.getByRole("cell", { name: "Ada" })).toBeTruthy();

    await user.click(screen.getByRole("tab", { name: "JSON" }));
    expect(container.querySelector("pre code")?.textContent).toBe(JSON.stringify(rows, null, 2));
  });

  it("shows only the latest run's input", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed([run([{ a: 1 }], { runIndex: 1 }), run([{ a: 2 }], { runIndex: 2 }), run([{ a: 0 }])]),
    });
    expect(screen.getByText("2")).toBeTruthy();
    expect(screen.queryByText("1")).toBeNull();
    expect(screen.queryByText("0")).toBeNull();
  });

  it("copies a field value and the input JSON", async () => {
    const user = userEvent.setup();
    const rows = [{ name: "Ada", nested: { x: [1] } }];
    renderWithProviders(<ReadonlyInputPanel />, { seedWorkflow: seed([run(rows)]) });

    await user.click(screen.getByRole("button", { name: "Copy value of name" }));
    expect(clipboard.copyToClipboard).toHaveBeenLastCalledWith("Ada");

    // Nested values show and copy as single-line JSON.
    expect(screen.getByText('{"x":[1]}')).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Copy value of nested" }));
    expect(clipboard.copyToClipboard).toHaveBeenLastCalledWith('{"x":[1]}');

    await user.click(screen.getByRole("tab", { name: "JSON" }));
    await user.click(screen.getByRole("button", { name: "Copy input JSON" }));
    expect(clipboard.copyToClipboard).toHaveBeenLastCalledWith(JSON.stringify(rows, null, 2));
  });

  it("renders primitive rows in cards and in the (value) column", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, { seedWorkflow: seed([run(["a", 5])]) });
    expect(screen.getByText("item 1:")).toBeTruthy();
    expect(screen.getByText("item 2:")).toBeTruthy();
    expect(screen.getByText("a")).toBeTruthy();
    expect(screen.getByText("5")).toBeTruthy();

    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getByRole("columnheader", { name: "(value)" })).toBeTruthy();
    expect(screen.getByRole("cell", { name: "a" })).toBeTruthy();
    expect(screen.getByRole("cell", { name: "5" })).toBeTruthy();
  });

  it("collapses and expands the body", async () => {
    const user = userEvent.setup();
    const onToggleCollapse = vi.fn();
    const { rerender } = renderWithProviders(<ReadonlyInputPanel onToggleCollapse={onToggleCollapse} />, {
      seedWorkflow: seed([run([{ name: "Ada" }])]),
    });
    expect(screen.getByText("Ada")).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Collapse input" }));
    expect(onToggleCollapse).toHaveBeenCalledTimes(1);

    rerender(<ReadonlyInputPanel isCollapsed onToggleCollapse={onToggleCollapse} />);
    expect(screen.queryByText("Ada")).toBeNull();
    expect(screen.getByRole("button", { name: "Expand input" })).toBeTruthy();
  });

  it("has no draggable elements and no mock-data controls, even with pinData in the editor", () => {
    const { container } = renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed([run([{ name: "Ada" }])], { editorMode: "editor", nodeExtra: { pinData: [{ pinned: 1 }] } }),
    });
    expect(container.querySelector('[draggable="true"]')).toBeNull();
    for (const text of ["Set mock data", "Edit mock data", "Remove mock data", "Mock data"]) {
      expect(screen.queryByText(text)).toBeNull();
      expect(screen.queryByRole("button", { name: text })).toBeNull();
    }
    // pinData does not replace the input.
    expect(screen.getByText("Ada")).toBeTruthy();
    expect(screen.queryByText("pinned:")).toBeNull();
  });

  it.each([
    ["no run for the node", [{ ...run([{ x: 1 }]), nodeId: "other" }]],
    ["an empty input", [run([])]],
    ["a null input", [run(null)]],
  ])("shows the empty states for %s", async (_label, executedNodes) => {
    const user = userEvent.setup();
    renderWithProviders(<ReadonlyInputPanel />, { seedWorkflow: seed(executedNodes, { editorMode: "version" }) });
    expect(screen.getByText("No runtime input schema available.")).toBeTruthy();
    await user.click(screen.getByRole("tab", { name: "Table" }));
    expect(screen.getByText("No runtime input data available.")).toBeTruthy();
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    expect(screen.getByText("No runtime input data available.")).toBeTruthy();
  });

  it("still shows a failed run's input, with no alert", () => {
    renderWithProviders(<ReadonlyInputPanel />, {
      seedWorkflow: seed([run([{ name: "Ada" }], { status: NodeExecutionStatus.Failed, error: "boom" })]),
    });
    expect(screen.getByText("Ada")).toBeTruthy();
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByText("boom")).toBeNull();
  });
});
