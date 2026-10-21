import { fireEvent, screen } from "@testing-library/react";
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
