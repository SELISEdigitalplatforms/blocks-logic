import { act, fireEvent, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithProviders } from "@/test-utils/test-providers/render";
import { NodeLibraryPanel } from "./node-library-panel";
import { NodeDefinitions } from "./node-definitions";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const openPanel = (store: any) => store.getState().openNodeLibraryPanel();

describe("NodeLibraryPanel", () => {
  it("renders the node options when the panel is open", () => {
    renderWithProviders(<NodeLibraryPanel />, { seedWorkflow: openPanel });
    expect(screen.getByText("Start your workflow")).toBeTruthy();
    expect(screen.getByText(NodeDefinitions[0].title)).toBeTruthy();
  });

  it("filters options by the search query", () => {
    renderWithProviders(<NodeLibraryPanel />, { seedWorkflow: openPanel });
    fireEvent.change(screen.getByPlaceholderText("Search"), {
      target: { value: "zzz-nonexistent" },
    });
    expect(screen.getByText(/No triggers found/)).toBeTruthy();
  });

  it("adds a node to the store when an option is selected", () => {
    let store: unknown;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    const selectable = NodeDefinitions.find((n) => !n.isComingSoon)!;
    fireEvent.click(screen.getByText(selectable.title));
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const nodesMap = (store as any).getState().nodesMap;
    expect(Object.keys(nodesMap).length).toBe(1);
  });
});

describe("NodeLibraryPanel per-node handles (SPEC5)", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const pick = (title: string, seed: (s: any) => void) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        seed(s);
        s.getState().openNodeLibraryPanel();
      },
    });
    fireEvent.click(screen.getByText(title));
    return store.getState();
  };

  it("H4: creates the node at v2 with its own copy of the template handle and no definition fields", () => {
    const state = pick("Set Field", () => undefined);
    const created = Object.values(state.nodesMap)[0] as Record<string, unknown>;
    expect(created.version).toBe("v2");
    expect(created.handle).toEqual({
      sources: [{ key: "source", label: "" }],
      targets: [{ key: "target", label: "" }],
    });
    const template = NodeDefinitions.find((d) => d.type === "setfield")!.handle;
    expect(created.handle).not.toBe(template);
    for (const field of ["icon", "title", "handleSpec", "description", "isComingSoon", "defaultName"]) {
      expect(created).not.toHaveProperty(field);
    }
  });

  it("H7: '+' from the If true handle connects if-true to the new node's first target", () => {
    const state = pick("Set Field", (s) => {
      s.getState().setWorkflow({
        itemId: "w1",
        nodes: [{ id: "if1", name: "If", category: "logic", type: "if", version: "v1", position: { x: 0, y: 0 }, data: {} }],
        edges: [],
      });
      s.getState().selectNode(s.getState().nodesMap.if1);
      s.getState().selectHandle("if-true");
    });
    const edges = Object.values(state.edgesMap) as Array<Record<string, unknown>>;
    expect(edges).toHaveLength(1);
    expect(edges[0]).toMatchObject({ source: "if1", sourceHandle: "if-true", targetHandle: "target" });
  });

  it("H7: without a picked handle, uses the selected node's next free source", () => {
    const state = pick("Set Field", (s) => {
      s.getState().setWorkflow({
        itemId: "w1",
        nodes: [
          { id: "if1", name: "If", category: "logic", type: "if", version: "v1", position: { x: 0, y: 0 }, data: {} },
          { id: "x", name: "X", category: "transform", type: "code", version: "v1", position: { x: 0, y: 0 }, data: {} },
        ],
        edges: [{ id: "e0", source: "if1", target: "x", sourceHandle: "if-true", targetHandle: "target" }],
      });
      s.getState().selectNode(s.getState().nodesMap.if1);
    });
    const added = (Object.values(state.edgesMap) as Array<Record<string, unknown>>).filter((e) => e.id !== "e0");
    expect(added).toHaveLength(1);
    expect(added[0]).toMatchObject({ source: "if1", sourceHandle: "if-false", targetHandle: "target" });
  });

  it("does not connect anything to a new trigger, which has no target", () => {
    const state = pick("Webhook", (s) => {
      s.getState().setWorkflow({
        itemId: "w1",
        nodes: [{ id: "x", name: "X", category: "transform", type: "code", version: "v1", position: { x: 0, y: 0 }, data: {} }],
        edges: [],
      });
      s.getState().selectNode(s.getState().nodesMap.x);
    });
    expect(Object.keys(state.edgesMap)).toHaveLength(0);
  });
});

describe("NodeLibraryPanel Switch (SPEC6)", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const addSwitch = (): any => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    fireEvent.click(screen.getByText("Switch"));
    return store;
  };

  it("H1: creates a v2 Switch with one rule and its derived handle", () => {
    const store = addSwitch();
    const created = Object.values(store.getState().nodesMap)[0] as Record<string, any>; // eslint-disable-line @typescript-eslint/no-explicit-any
    expect(created).toMatchObject({ type: "switch", category: "logic", version: "v2" });
    expect(created.parameters.mode).toBe("rules");
    expect(created.parameters.rules).toHaveLength(1);
    const key = created.parameters.rules[0].handleKey;
    expect(key).toMatch(/^switch-handle-source-[a-z0-9]{6}$/);
    expect(created.handle).toEqual({ targets: [{ key: "target", label: "" }], sources: [{ key, label: "" }] });
  });

  it("two new Switch nodes never share parameters or keys", () => {
    const store = addSwitch();
    act(() => store.getState().openNodeLibraryPanel());
    fireEvent.click(screen.getByText("Switch"));
    const [a, b] = Object.values(store.getState().nodesMap) as Array<Record<string, any>>; // eslint-disable-line @typescript-eslint/no-explicit-any
    expect(a.parameters).not.toBe(b.parameters);
    expect(a.parameters.rules[0].handleKey).not.toBe(b.parameters.rules[0].handleKey);
  });
});

describe("NodeLibraryPanel Filter (SPEC9)", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const open = (): any => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    return store;
  };

  it("H1: lists Filter as a logic node with the Filter icon and its description", () => {
    open();
    const option = screen.getByText("Filter").closest("button") as HTMLElement;
    expect(option.textContent).toContain("Keep only the items that match the conditions.");
    expect(option.querySelector("svg.lucide-filter")?.getAttribute("class")).toContain("text-success");
    expect(NodeDefinitions.find((d) => d.type === "filter")).toMatchObject({ category: "logic", version: "v2" });
  });

  it("H2/H3: creates a v2 Filter named Filter with one target, one source and its default parameters", () => {
    const store = open();
    fireEvent.click(screen.getByText("Filter"));
    const created = Object.values(store.getState().nodesMap)[0] as Record<string, unknown>;
    expect(created).toMatchObject({ name: "Filter", type: "filter", category: "logic", version: "v2" });
    expect(created.handle).toEqual({ sources: [{ key: "source", label: "" }], targets: [{ key: "target", label: "" }] });
    expect(created.parameters).toEqual({ conditionType: "and", conditions: [{ left: "", operator: "equals", right: "", type: "string" }] });
  });

  it("C11: two new Filter nodes never share parameters", () => {
    const store = open();
    fireEvent.click(screen.getByText("Filter"));
    act(() => store.getState().openNodeLibraryPanel());
    fireEvent.click(screen.getByText("Filter"));
    const [a, b] = Object.values(store.getState().nodesMap) as Array<Record<string, any>>; // eslint-disable-line @typescript-eslint/no-explicit-any
    expect(b.name).toBe("Filter2");
    expect(a.parameters).not.toBe(b.parameters);
    expect(a.parameters.conditions).not.toBe(b.parameters.conditions);
  });
});

describe("NodeLibraryPanel Split Out (SPEC2)", () => {
  it("H10: lists Split Out and creates a v2 node with one target and one source", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    fireEvent.click(screen.getByText("Split Out"));
    const created = Object.values(store.getState().nodesMap)[0] as Record<string, unknown>;
    expect(created).toMatchObject({ type: "splitOut", category: "transform", version: "v2" });
    expect(created.handle).toEqual({ sources: [{ key: "source", label: "" }], targets: [{ key: "target", label: "" }] });
  });
});

describe("NodeLibraryPanel Limit (SPEC10)", () => {
  it("H1/H2: lists Limit in Transform and creates a v2 node with one input and one output", () => {
    const limit = NodeDefinitions.find((d) => d.id === "transform-limit-v2")!;
    expect(limit).toMatchObject({
      title: "Limit",
      description: "Keep at most a set number of items, from the start or the end of the list.",
      type: "limit",
      category: "transform",
      version: "v2",
      defaultName: "Limit",
    });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    fireEvent.click(screen.getByText("Limit"));
    const created = Object.values(store.getState().nodesMap)[0] as Record<string, unknown>;
    expect(created).toMatchObject({ type: "limit", category: "transform", version: "v2", name: "Limit", parameters: { maxItems: 1, keep: "first" } });
    expect(created.handle).toEqual({ sources: [{ key: "source", label: "" }], targets: [{ key: "target", label: "" }] });
  });

  it("C14: two Limit nodes keep independent parameters", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    fireEvent.click(screen.getByText("Limit"));
    act(() => store.getState().openNodeLibraryPanel());
    fireEvent.click(screen.getByText("Limit"));
    const [first, second] = Object.values(store.getState().nodesMap) as { id: string; parameters: Record<string, unknown> }[];
    expect(second).toBeDefined();
    store.getState().updateNodeParameters(first.id, { maxItems: 7, keep: "last" });
    expect(store.getState().nodesMap[second.id].parameters).toEqual({ maxItems: 1, keep: "first" });
  });
});

describe("NodeLibraryPanel Merge (SPEC3)", () => {
  it("H8/H11: creates a v2 Merge with 2 inputs; '+' with two selected nodes connects both to input 1", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().setWorkflow({
          itemId: "w1",
          nodes: [
            { id: "a", name: "A", category: "transform", type: "code", version: "v1", position: { x: 0, y: 0 }, data: {}, selected: true },
            { id: "b", name: "B", category: "transform", type: "code", version: "v1", position: { x: 0, y: 0 }, data: {}, selected: true },
          ],
          edges: [],
        });
        s.getState().openNodeLibraryPanel();
      },
    });
    fireEvent.click(screen.getByText("Merge"));
    const merge = Object.values(store.getState().nodesMap).find((n: any) => n.type === "merge") as any; // eslint-disable-line @typescript-eslint/no-explicit-any
    expect(merge).toMatchObject({ version: "v2", parameters: { mode: "append", numberInputs: 2 }, settings: {} });
    expect(merge.handle.targets.map((t: { key: string }) => t.key)).toEqual(["merge-handle-target-1", "merge-handle-target-2"]);
    const edges = Object.values(store.getState().edgesMap) as Array<Record<string, unknown>>;
    expect(edges.map((e) => [e.source, e.targetHandle]).sort()).toEqual([["a", "merge-handle-target-1"], ["b", "merge-handle-target-1"]]);
  });

  it("SPEC8 H13/C14: adds one v2 Error Trigger, then disables its row", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let store: any;
    renderWithProviders(<NodeLibraryPanel />, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      seedWorkflow: (s: any) => {
        store = s;
        s.getState().openNodeLibraryPanel();
      },
    });
    fireEvent.click(screen.getByText("Error Trigger"));
    const nodes = () => Object.values(store.getState().nodesMap) as Record<string, unknown>[];
    expect(nodes()).toHaveLength(1);
    expect(nodes()[0]).toMatchObject({ type: "errorTrigger", category: "trigger", version: "v2", name: "Error Trigger" });
    expect(nodes()[0].handle).toEqual({ sources: [{ key: "source", label: "" }], targets: [] });

    act(() => store.getState().openNodeLibraryPanel());
    const row = screen.getByText("Error Trigger").closest("[aria-disabled]");
    expect(row?.getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(screen.getByText("Error Trigger"));
    expect(nodes().filter((n) => n.type === "errorTrigger")).toHaveLength(1);
  });
});
