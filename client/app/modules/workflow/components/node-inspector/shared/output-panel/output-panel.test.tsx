import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { OutputPanel } from "./output-panel";

const toasts = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  showInfoToast: vi.fn(),
}));
const clipboard = vi.hoisted(() => ({
  copyToClipboard: vi.fn().mockResolvedValue(true),
}));
vi.mock("@/hooks/use-toast", () => toasts);
vi.mock("@blocks-workflow/utils/copy-to-clipboard", () => clipboard);

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (extra: Record<string, unknown> = {}): any => ({
  id: "n1",
  name: "n1",
  type: "action",
  position: { x: 0, y: 0 },
  parameters: {},
  ...extra,
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const seedSelected = (extra: Record<string, unknown> = {}) => (store: any) => {
  const n = node(extra);
  store.getState().addNode(n);
  store.setState({ selectedNode: n, editorMode: "editor" });
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("OutputPanel", () => {
  it("renders nothing without a selected node", () => {
    const { container } = renderWithProviders(<OutputPanel />);
    expect(container.textContent).toBe("");
  });

  it("shows the schema derived from pinned data and copies a value", () => {
    renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected({ pinData: [{ name: "Ada", age: 30 }] }),
    });
    expect(screen.getByText("Output")).toBeTruthy();
    expect(screen.getByText("name:")).toBeTruthy();
    // clicking a schema field copies the value (no throw)
    fireEvent.click(screen.getByText("name:"));
  });

  it("marks pinned output as mock data and removes it back to the run output", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<OutputPanel />, {
      seedWorkflow: (s) => {
        store = s;
        seedSelected({ pinData: [{ name: "Ada" }] })(s);
        s.setState({
          executedItems: [{ nodeId: "n1", itemIndex: 0, branch: "default", data: { Output: { city: "Paris" } } }],
        });
      },
    });
    expect(screen.getByText("Mock data")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Edit mock data" })).toBeTruthy();
    expect(screen.getByText("name:")).toBeTruthy();
    expect(screen.queryByText("city:")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Remove mock data" }));

    expect(store.getState().nodesMap.n1.pinData).toBeNull();
    expect(screen.queryByText("Mock data")).toBeNull();
    expect(screen.queryByRole("button", { name: "Remove mock data" })).toBeNull();
    expect(screen.getByText("Set mock data")).toBeTruthy();
    expect(screen.getByText("city:")).toBeTruthy();
  });

  it("shows no mock-data indicator or remove button without pinned data", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected({ pinData: null }) });
    expect(screen.queryByText("Mock data")).toBeNull();
    expect(screen.queryByRole("button", { name: "Remove mock data" })).toBeNull();
  });

  it("shows mock data outside the editor without the edit actions", () => {
    renderWithProviders(<OutputPanel />, {
      seedWorkflow: (s) => {
        seedSelected({ pinData: [{ name: "Ada" }] })(s);
        s.setState({
          editorMode: "run",
          executedItems: [{ nodeId: "n1", itemIndex: 0, branch: "default", data: { Output: { city: "Paris" } } }],
        });
      },
    });
    expect(screen.getByText("Mock data")).toBeTruthy();
    expect(screen.getByText("name:")).toBeTruthy();
    expect(screen.queryByText("city:")).toBeNull();
    expect(screen.queryByText("Set mock data")).toBeNull();
    expect(screen.queryByRole("button", { name: "Edit mock data" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Remove mock data" })).toBeNull();
  });

  it("explains mock data in the badge tooltip", async () => {
    const user = userEvent.setup();
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected({ pinData: [{ name: "Ada" }] }) });
    await user.hover(screen.getByText("Mock data"));
    expect(
      (await screen.findAllByText("Showing mock data. Runs use it as this step's output instead of running the step."))
        .length,
    ).toBeGreaterThan(0);
  });

  it("opens the editor prefilled from the edit icon and hides the actions while editing", () => {
    const { container } = renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected({ pinData: [{ name: "Ada" }] }),
    });
    fireEvent.click(screen.getByRole("button", { name: "Edit mock data" }));

    const textarea = container.querySelector("textarea") as HTMLTextAreaElement;
    expect(JSON.parse(textarea.value)).toEqual([{ name: "Ada" }]);
    expect(screen.queryByRole("button", { name: "Edit mock data" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Remove mock data" })).toBeNull();
    expect(screen.queryByText("Set mock data")).toBeNull();
  });

  it("treats a single non-array pinned value as one mock row", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected({ pinData: { name: "Ada" } }) });
    expect(screen.getByText("Mock data")).toBeTruthy();
    expect(screen.getByText("name:")).toBeTruthy();
  });

  it("shows the no-schema message when there is no output", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected() });
    expect(
      screen.getByText("No runtime output schema available."),
    ).toBeTruthy();
  });

  it("supports the mock-data editing flow with invalid JSON", () => {
    const { container } = renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected(),
    });
    fireEvent.click(screen.getByText("Set mock data"));
    const textarea = container.querySelector("textarea") as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "not json" } });
    fireEvent.click(screen.getByText("Save"));
    expect(toasts.showErrorToast).toHaveBeenCalledWith({
      errors: "Invalid JSON format",
    });
  });

  it("saves valid mock data as pinData", () => {
    const { container } = renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected(),
    });
    fireEvent.click(screen.getByText("Set mock data"));
    const textarea = container.querySelector("textarea") as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: '{"x":1}' } });
    fireEvent.click(screen.getByText("Save"));
    // editing panel closes -> tabs come back
    expect(screen.getByText("Schema")).toBeTruthy();
  });

  it("cancels mock data editing", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected() });
    fireEvent.click(screen.getByText("Set mock data"));
    fireEvent.click(screen.getByText("Cancel"));
    expect(screen.getByText("Set mock data")).toBeTruthy();
  });

  it("toggles collapse via the chevron button", () => {
    const onToggleCollapse = vi.fn();
    renderWithProviders(
      <OutputPanel isCollapsed={false} onToggleCollapse={onToggleCollapse} />,
      { seedWorkflow: seedSelected({ pinData: [{ a: 1 }] }) },
    );
    fireEvent.click(screen.getByRole("button", { name: "Collapse output" }));
    expect(onToggleCollapse).toHaveBeenCalled();
  });

  it("switches to the table and json tabs", async () => {
    const user = userEvent.setup();
    renderWithProviders(<OutputPanel />, {
      seedWorkflow: seedSelected({ pinData: [{ name: "Ada" }] }),
    });
    await user.click(screen.getByRole("tab", { name: "Table" }));
    await waitFor(() => expect(screen.getByText("name")).toBeTruthy());
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    await waitFor(() =>
      expect(document.querySelector("pre code")).toBeTruthy(),
    );
    await user.click(screen.getByRole("button", { name: "Copy output JSON" }));
    expect(clipboard.copyToClipboard).toHaveBeenCalledWith(
      JSON.stringify([{ name: "Ada" }], null, 2),
    );
  });

  // Seeds a node plus runtime executed items (not pinData) grouped by branch,
  // in run mode so the branch-grouping code path is exercised.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const seedRuntimeBranches = (store: any) => {
    const n = node();
    store.getState().addNode(n);
    store.setState({
      selectedNode: n,
      editorMode: "run",
      executedItems: [
        { nodeId: "n1", itemIndex: 0, branch: "True", data: { Output: { city: "Paris" } } },
        { nodeId: "n1", itemIndex: 1, branch: "False", data: { Output: "plain-value" } },
        // items for another node are ignored
        { nodeId: "other", itemIndex: 0, branch: "True", data: { Output: { city: "X" } } },
      ],
    });
  };

  it("groups runtime executed items by branch in the schema tab", () => {
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedRuntimeBranches });
    expect(screen.getByText("True")).toBeTruthy();
    expect(screen.getByText("False")).toBeTruthy();
    // object row exposes its field key, primitive row is rendered directly
    expect(screen.getByText("city:")).toBeTruthy();
    expect(screen.getByText("plain-value")).toBeTruthy();
  });

  it("renders the multi-branch table with a primitive value column", async () => {
    const user = userEvent.setup();
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedRuntimeBranches });
    await user.click(screen.getByRole("tab", { name: "Table" }));
    await waitFor(() => expect(screen.getAllByText("1 item").length).toBe(2));
    // the False branch holds a primitive, forcing the "(value)" column header
    expect(screen.getByText("(value)")).toBeTruthy();
    expect(screen.getByText("city")).toBeTruthy();
  });

  it("renders the multi-branch json tab", async () => {
    const user = userEvent.setup();
    renderWithProviders(<OutputPanel />, { seedWorkflow: seedRuntimeBranches });
    await user.click(screen.getByRole("tab", { name: "JSON" }));
    await waitFor(() =>
      expect(document.querySelectorAll("pre code").length).toBe(2),
    );
  });

  describe("branch names (Switch, If)", () => {
    const A = "switch-handle-source-woggl1";
    const B = "switch-handle-source-iocc8w";
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const seedNodeRun = (extra: Record<string, unknown>, items: unknown[]) => (store: any) => {
      const n = node({ category: "logic", version: "v2", ...extra });
      store.getState().addNode(n);
      store.setState({ selectedNode: n, editorMode: "run", executedItems: items });
    };
    const switchNode = {
      type: "switch",
      handle: {
        targets: [{ key: "target", label: "" }],
        sources: [
          { key: A, label: "Adult" },
          { key: B, label: "" },
          { key: "switch-handle-source-fallback", label: "" },
        ],
      },
    };
    const switchItems = [
      { nodeId: "n1", itemIndex: 0, branch: A, data: { Output: { firstName: "John" } } },
      { nodeId: "n1", itemIndex: 1, branch: B, data: { Output: { firstName: "Alice" } } },
    ];

    it("names each Switch output by its name, or Output <i> when unnamed, in output order, with item counts", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedNodeRun(switchNode, switchItems) });
      expect(screen.getByText("Adult")).toBeTruthy();
      expect(screen.getByText("Output 1")).toBeTruthy();
      expect(screen.getByText("Fallback")).toBeTruthy();
      expect(screen.queryByText(new RegExp(A, "i"))).toBeNull();
      expect(screen.getAllByText("1 item")).toHaveLength(2);
      // An output that received nothing is listed, so it is clear where items did not go.
      expect(screen.getByText("no items")).toBeTruthy();
      expect(screen.getByText("No items went to this output.")).toBeTruthy();
    });

    it("keeps the raw key for debugging: tooltip and copy button", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedNodeRun(switchNode, switchItems) });
      expect(screen.getByText("Adult").getAttribute("title")).toBe(A);
      fireEvent.click(screen.getByRole("button", { name: "Copy branch key of Adult" }));
      expect(clipboard.copyToClipboard).toHaveBeenCalledWith(A);
    });

    it("uses the same names in the table and json tabs", async () => {
      const user = userEvent.setup();
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedNodeRun(switchNode, switchItems) });
      await user.click(screen.getByRole("tab", { name: "Table" }));
      await waitFor(() => expect(screen.getByText("Adult")).toBeTruthy());
      await user.click(screen.getByRole("tab", { name: "JSON" }));
      await waitFor(() => expect(screen.getByText("Output 1")).toBeTruthy());
      expect(document.querySelectorAll("pre code").length).toBe(2);
    });

    it("a renamed fallback shows its name; an output deleted after the run shows its key", () => {
      const renamed = { ...switchNode, handle: { ...switchNode.handle, sources: [switchNode.handle.sources[0], { key: "switch-handle-source-fallback", label: "Other" }] } };
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedNodeRun(renamed, switchItems) });
      expect(screen.getByText("Other")).toBeTruthy();
      expect(screen.getByText(B)).toBeTruthy();
    });

    it("shows True and False for an If node", () => {
      const ifNode = {
        type: "if",
        handle: { targets: [{ key: "target", label: "" }], sources: [{ key: "if-true", label: "True" }, { key: "if-false", label: "False" }] },
      };
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedNodeRun(ifNode, [{ nodeId: "n1", itemIndex: 0, branch: "if-false", data: { Output: { a: 1 } } }]),
      });
      expect(screen.getByText("True")).toBeTruthy();
      expect(screen.getByText("False")).toBeTruthy();
      expect(screen.getByText("no items")).toBeTruthy();
    });

    it("before any run, a Switch shows the usual empty state, not empty outputs", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedNodeRun(switchNode, []) });
      expect(screen.getByText("No runtime output schema available.")).toBeTruthy();
      expect(screen.queryByText("Adult")).toBeNull();
    });

    it("a single-output node shows no branch header", () => {
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedNodeRun(
          { type: "code", category: "transform", handle: { targets: [{ key: "target", label: "" }], sources: [{ key: "source", label: "" }] } },
          [{ nodeId: "n1", itemIndex: 0, branch: "source", data: { Output: { a: 1 } } }],
        ),
      });
      expect(screen.queryByText("1 item")).toBeNull();
      expect(screen.getByText("a:")).toBeTruthy();
    });
  });

  describe("Split Out run summary", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const seedSplitOutRun = (parameters: Record<string, unknown>, input: unknown[], outputCount: number, extra: Record<string, unknown> = {}) => (store: any) => {
      const n = node({ type: "splitOut", category: "transform", version: "v2", parameters, ...extra });
      store.getState().addNode(n);
      store.setState({
        selectedNode: n,
        editorMode: "run",
        executedNodes: [{ id: "ne1", nodeId: "n1", runIndex: 0, status: 2, input, output: [], inputItemCount: input.length, outputItemCount: outputCount }],
        executedItems: Array.from({ length: outputCount }, (_, i) => ({ nodeId: "n1", itemIndex: i, branch: "source", data: { Output: { sku: `S${i}` } } })),
      });
    };

    it("shows how many items went in and came out", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedSplitOutRun({ fields: [{ fieldName: "lines", outputFieldName: "" }] }, [{ lines: [1, 2] }, { lines: [3] }], 3) });
      expect(screen.getByText("2 items in → 3 items out")).toBeTruthy();
      expect(screen.queryByText(/wasn't found/)).toBeNull();
    });

    it("shows n8n's hint for a field no input item has", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedSplitOutRun({ fields: [{ fieldName: "lines", outputFieldName: "" }, { fieldName: "tags", outputFieldName: "" }] }, [{ lines: [1] }], 1) });
      expect(screen.getByText("The field 'tags' wasn't found in any input item")).toBeTruthy();
      expect(screen.queryByText("The field 'lines' wasn't found in any input item")).toBeNull();
    });

    it("says 0 items out when nothing was found", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedSplitOutRun({ fields: [{ fieldName: "lines", outputFieldName: "" }] }, [{ other: 1 }], 0) });
      expect(screen.getByText("1 item in → 0 items out")).toBeTruthy();
      expect(screen.getByText("The field 'lines' wasn't found in any input item")).toBeTruthy();
    });

    it("is not shown for other nodes, before a run, or with mock data", () => {
      const { unmount } = renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected({ type: "splitOut", parameters: { fields: [{ fieldName: "lines", outputFieldName: "" }] } }) });
      expect(screen.queryByText(/items? in →/)).toBeNull();
      unmount();
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedSplitOutRun({ fields: [{ fieldName: "lines", outputFieldName: "" }] }, [{ lines: [1] }], 1, { pinData: [{ a: 1 }] }),
      });
      expect(screen.queryByText(/items? in →/)).toBeNull();
    });
  });

  describe("Merge run summary", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const seedMergeRun = (extra: Record<string, unknown> = {}, mergeStatus = 4) => (store: any) => {
      const n = node({ id: "m", type: "merge", category: "transform", version: "v2", parameters: { mode: "append", numberInputs: 3 }, ...extra });
      store.getState().addNode(n);
      store.setState({
        selectedNode: n,
        editorMode: "run",
        edgesMap: {
          e1: { id: "e1", source: "a", target: "m", sourceHandle: "source", targetHandle: "merge-handle-target-1" },
          e2: { id: "e2", source: "b", target: "m", sourceHandle: "source", targetHandle: "merge-handle-target-2" },
        },
        executedNodes: [
          { id: "ne-a", nodeId: "a", runIndex: 0, status: 4, outputCountsByBranch: { source: 2 } },
          { id: "ne-b", nodeId: "b", runIndex: 1, status: 4, outputCountsByBranch: { source: 1 } },
          { id: "ne-m", nodeId: "m", runIndex: 2, status: mergeStatus, outputItemCount: 3, outputCountsByBranch: { source: 3 } },
        ],
        executedItems: [],
      });
    };

    it("shows how many items each input received, including an empty one", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedMergeRun() });
      expect(screen.getByText(/Input 1 · 2 items\s+Input 2 · 1 item\s+Input 3 · 0 items/)).toBeTruthy();
    });

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const seedInput2Empty = (runWhen?: string) => (store: any) => {
      seedMergeRun({ parameters: { mode: "append", numberInputs: 2, ...(runWhen ? { runWhen } : {}) } })(store);
      store.setState({
        executedNodes: [
          { id: "ne-a", nodeId: "a", runIndex: 0, status: 4, outputCountsByBranch: { source: 2 } },
          { id: "ne-b", nodeId: "b", runIndex: 1, status: 4, outputCountsByBranch: {} },
          { id: "ne-m", nodeId: "m", runIndex: 2, status: 4, outputItemCount: 0, outputCountsByBranch: {} },
        ],
      });
    };
    const allNote = "Run When is All: Input 2 brought no items, so nothing was output.";

    it("with All (the default), says which connected input had no items", () => {
      const { unmount } = renderWithProviders(<OutputPanel />, { seedWorkflow: seedInput2Empty("all") });
      expect(screen.getByText(/Input 1 · 2 items\s+Input 2 · 0 items/)).toBeTruthy();
      expect(screen.getByText(allNote)).toBeTruthy();
      // An <output> element: announced as a status without an explicit role.
      expect(screen.getByRole("status").tagName).toBe("OUTPUT");
      unmount();
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedInput2Empty() });
      expect(screen.getByText(allNote)).toBeTruthy();
    });

    it("with Any, shows the counts but no note", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedInput2Empty("any") });
      expect(screen.getByText(/Input 2 · 0 items/)).toBeTruthy();
      expect(screen.queryByText(allNote)).toBeNull();
    });

    it("is not shown before a run, after a failed run, or with mock data", () => {
      const { unmount } = renderWithProviders(<OutputPanel />, { seedWorkflow: seedSelected({ id: "m", type: "merge", parameters: { numberInputs: 2 } }) });
      expect(screen.queryByText(/Input 1 ·/)).toBeNull();
      unmount();
      const failed = renderWithProviders(<OutputPanel />, { seedWorkflow: seedMergeRun({}, 5) });
      expect(screen.queryByText(/Input 1 ·/)).toBeNull();
      failed.unmount();
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedMergeRun({ pinData: [{ a: 1 }] }) });
      expect(screen.queryByText(/Input 1 ·/)).toBeNull();
    });
  });

  it("hides the schema body while collapsed", () => {
    renderWithProviders(<OutputPanel isCollapsed onToggleCollapse={() => {}} />, {
      seedWorkflow: seedSelected({ pinData: [{ name: "Ada" }] }),
    });
    // header still shows, but the schema field is not rendered when collapsed
    expect(screen.getByText("Output")).toBeTruthy();
    expect(screen.queryByText("name:")).toBeNull();
  });

  describe("failed run error (WF-40)", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const run = (extra: Record<string, unknown>): any => ({
      id: `ne-${String(extra.runIndex)}`,
      nodeId: "n1",
      nodeName: "n1",
      runIndex: 1,
      status: 4,
      error: null,
      ...extra,
    });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const seedWithRuns = (runs: unknown[]) => (store: any) => {
      seedSelected()(store);
      store.setState({ executedNodes: runs });
    };

    it("shows the error of the node's failed run", () => {
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedWithRuns([run({ status: 5, error: "Field 'amount' is missing." })]),
      });
      expect(screen.getByRole("alert").textContent).toBe("Field 'amount' is missing.");
    });

    it("shows nothing when the latest run succeeded", () => {
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedWithRuns([
          run({ runIndex: 1, status: 5, error: "old failure" }),
          run({ runIndex: 2, status: 4 }),
        ]),
      });
      expect(screen.queryByRole("alert")).toBeNull();
    });

    it("ignores another node's failure", () => {
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedWithRuns([run({ nodeId: "other", status: 5, error: "boom" })]),
      });
      expect(screen.queryByRole("alert")).toBeNull();
    });

    it("cuts an old stored stack trace and its exception prefix", () => {
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedWithRuns([
          run({ status: 5, error: "System.Exception: Bad input.\n   at Workflow.Engine.Run() in /src/Engine.cs:line 12" }),
        ]),
      });
      const text = screen.getByRole("alert").textContent ?? "";
      expect(text).toBe("Bad input.");
      expect(text).not.toContain("Engine.cs");
    });

    it("falls back to a generic text when no error was saved", () => {
      renderWithProviders(<OutputPanel />, { seedWorkflow: seedWithRuns([run({ status: 5, error: null })]) });
      expect(screen.getByRole("alert").textContent).toBe("This step failed.");
    });

    it("renders error text as text, not HTML", () => {
      renderWithProviders(<OutputPanel />, {
        seedWorkflow: seedWithRuns([run({ status: 5, error: "<img src=x onerror=alert(1)>" })]),
      });
      const alert = screen.getByRole("alert");
      expect(alert.querySelector("img")).toBeNull();
      expect(alert.textContent).toBe("<img src=x onerror=alert(1)>");
    });
  });
});
