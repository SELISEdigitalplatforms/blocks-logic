import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createWorkflowStore, type WorkflowStore } from "./workflow-store";
import { NodeDefinitions } from "../components/node-library-panel/node-definitions";
import type { WorkflowNodeDefinition } from "../models/node.model";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (id: string, extra: Record<string, unknown> = {}): any => ({
  id,
  name: id,
  type: "action",
  position: { x: 0, y: 0 },
  parameters: {},
  selected: false,
  data: {},
  ...extra,
});

let store: WorkflowStore;
const state = () => store.getState();

beforeEach(() => {
  store = createWorkflowStore();
});

describe("node operations", () => {
  it("adds a node and marks unsaved changes", () => {
    state().addNode(node("a"));
    expect(state().nodesMap.a).toBeDefined();
    expect(state().hasUnsavedChanges).toBe(true);
  });

  it("gives duplicate names a numeric suffix on add", () => {
    state().addNode(node("a", { name: "Task" }));
    state().addNode(node("b", { name: "Task" }));
    expect(state().nodesMap.b.name).toBe("Task 1");
    state().addNode(node("c", { name: "Task" }));
    expect(state().nodesMap.c.name).toBe("Task 2");
  });

  it("updates a node and syncs the selected node reference", () => {
    state().addNode(node("a"));
    state().selectNode(state().nodesMap.a);
    state().updateNode("a", { name: "renamed" });
    expect(state().nodesMap.a.name).toBe("renamed");
    expect(state().selectedNode?.name).toBe("renamed");
  });

  it("update on a missing node is a no-op", () => {
    state().updateNode("missing", { name: "x" });
    expect(state().nodesMap.missing).toBeUndefined();
  });

  it("deletes a node and its connected edges", () => {
    state().addNode(node("a"));
    state().addNode(node("b"));
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    state().deleteNode("a");
    expect(state().nodesMap.a).toBeUndefined();
    expect(Object.keys(state().edgesMap)).toHaveLength(0);
  });

  it("duplicates a node with an offset position and unique name", () => {
    state().addNode(node("a", { name: "Task", position: { x: 10, y: 20 } }));
    state().duplicateNode("a");
    const dup = Object.values(state().nodesMap).find((n) => n.id !== "a");
    expect(dup?.name).toBe("Task 1");
    expect(dup?.position).toEqual({ x: 60, y: 120 });
  });

  it("duplicate on a missing node is a no-op", () => {
    state().duplicateNode("missing");
    expect(Object.keys(state().nodesMap)).toHaveLength(0);
  });
});

describe("copy and paste", () => {
  it("copies a single node", () => {
    state().addNode(node("a"));
    state().copyNode("a");
    expect(state().copiedNodes).toHaveLength(1);
    expect(state().copiedEdges).toHaveLength(0);
  });

  it("copyNode ignores a missing node", () => {
    state().copyNode("missing");
    expect(state().copiedNodes).toHaveLength(0);
  });

  it("copies selected nodes and the edges between them", () => {
    state().addNode(node("a", { selected: true }));
    state().addNode(node("b", { selected: true }));
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    state().copySelectedNodes();
    expect(state().copiedNodes).toHaveLength(2);
    expect(state().copiedEdges).toHaveLength(1);
  });

  it("paste is a no-op when nothing is copied", () => {
    state().pasteNodes();
    expect(Object.keys(state().nodesMap)).toHaveLength(0);
  });

  it("pastes copied nodes and remapped edges at a position", () => {
    state().addNode(node("a", { selected: true, name: "Task" }));
    state().addNode(node("b", { selected: true, name: "Other" }));
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    state().copySelectedNodes();
    state().pasteNodes({ x: 5, y: 5 });
    const pasted = Object.values(state().nodesMap).filter((n) => n.selected);
    expect(pasted).toHaveLength(2);
    // one edge existed, one remapped edge created
    expect(Object.keys(state().edgesMap).length).toBeGreaterThanOrEqual(2);
  });

  it("pastes with the default offset when no position is given", () => {
    state().addNode(node("a", { selected: true, position: { x: 1, y: 1 } }));
    state().copySelectedNodes();
    state().pasteNodes();
    const pasted = Object.values(state().nodesMap).find((n) => n.selected);
    expect(pasted?.position).toEqual({ x: 51, y: 101 });
  });
});

describe("edge operations and react flow handlers", () => {
  it("creates and deletes an edge", () => {
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    const id = "xy-edge__a-b";
    expect(state().edgesMap[id]).toBeDefined();
    state().deleteEdge(id);
    expect(state().edgesMap[id]).toBeUndefined();
  });

  it("onConnect adds an edge and dirties the workflow", () => {
    state().onConnect({
      source: "a",
      target: "b",
      sourceHandle: null,
      targetHandle: null,
    });
    expect(Object.keys(state().edgesMap)).toHaveLength(1);
    expect(state().hasUnsavedChanges).toBe(true);
  });

  it("onNodesChange applies a position change and dirties", () => {
    state().addNode(node("a"));
    state().onNodesChange([
      { id: "a", type: "position", position: { x: 5, y: 6 }, dragging: false },
    ]);
    expect(state().nodesMap.a.position).toEqual({ x: 5, y: 6 });
    expect(state().hasUnsavedChanges).toBe(true);
  });

  it("onNodesChange deselecting the selected node closes the modal", () => {
    state().addNode(node("a"));
    state().selectNode(state().nodesMap.a);
    state().openConfigModal();
    state().onNodesChange([{ id: "a", type: "select", selected: false }]);
    expect(state().selectedNode).toBeNull();
    expect(state().isConfigModalOpen).toBe(false);
  });

  it("onNodesChange auto-selects edges between two selected nodes", () => {
    state().addNode(node("a"));
    state().addNode(node("b"));
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    state().onNodesChange([
      { id: "a", type: "select", selected: true },
      { id: "b", type: "select", selected: true },
    ]);
    expect(state().edgesMap["xy-edge__a-b"].selected).toBe(true);
  });

  it("onEdgesChange removes an edge and dirties", () => {
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    state().onEdgesChange([{ id: "xy-edge__a-b", type: "remove" }]);
    expect(state().edgesMap["xy-edge__a-b"]).toBeUndefined();
    expect(state().hasUnsavedChanges).toBe(true);
  });
});

describe("selection, handles, panels and modals", () => {
  it("selectNode marks only the target selected", () => {
    state().addNode(node("a"));
    state().addNode(node("b"));
    state().selectNode(state().nodesMap.a);
    expect(state().nodesMap.a.selected).toBe(true);
    expect(state().nodesMap.b.selected).toBe(false);
    expect(state().selectedNode?.id).toBe("a");
  });

  it("selectNode(null) clears selection", () => {
    state().addNode(node("a"));
    state().selectNode(null);
    expect(state().selectedNode).toBeNull();
  });

  it("deselectNode clears all selections", () => {
    state().addNode(node("a", { selected: true }));
    state().selectNode(state().nodesMap.a);
    state().deselectNode();
    expect(state().selectedNode).toBeNull();
    expect(state().nodesMap.a.selected).toBe(false);
  });

  it("deselectAllEdges unselects every edge", () => {
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    store.setState((s) => ({
      edgesMap: {
        "xy-edge__a-b": { ...s.edgesMap["xy-edge__a-b"], selected: true },
      },
    }));
    state().deselectAllEdges();
    expect(state().edgesMap["xy-edge__a-b"].selected).toBe(false);
  });

  it("selects and deselects handles", () => {
    state().selectHandle("h1");
    expect(state().selectedHandle).toBe("h1");
    state().deselectHandle();
    expect(state().selectedHandle).toBeNull();
  });

  it("opens and closes the config modal and library panel", () => {
    state().openConfigModal();
    expect(state().isConfigModalOpen).toBe(true);
    state().closeConfigModal();
    expect(state().isConfigModalOpen).toBe(false);
    state().openNodeLibraryPanel();
    expect(state().isPanelOpen).toBe(true);
    state().selectHandle("h");
    state().closeNodeLibraryPanel();
    expect(state().isPanelOpen).toBe(false);
    expect(state().selectedHandle).toBeNull();
  });
});

describe("workflow lifecycle", () => {
  it("setWorkflow hydrates nodes, edges and metadata", () => {
    state().setWorkflow({
      itemId: "wf-1",
      name: "My Flow",
      isPublished: true,
      nodes: [node("a")],
      edges: [{ id: "e1", source: "a", target: "a" }],
      items: [{ id: "i1" }],
      nodeExecutions: [{ nodeId: "a" }],
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);
    expect(state().workflowId).toBe("wf-1");
    expect(state().workflowName).toBe("My Flow");
    expect(state().isPublished).toBe(true);
    expect(state().nodesMap.a).toBeDefined();
    expect(state().executedItems).toHaveLength(1);
  });

  it("setWorkflow preserves prior execution data on refetch of same workflow", () => {
    store.setState({
      workflowId: "wf-1",
      executedItems: [{ id: "old" }],
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);
    state().setWorkflow({
      itemId: "wf-1",
      name: "Flow",
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);
    expect(state().executedItems).toEqual([{ id: "old" }]);
  });

  it("setWorkflow with empty payload defaults to empty maps", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    state().setWorkflow({} as any);
    expect(state().nodesMap).toEqual({});
    expect(state().workflowId).toBeNull();
  });

  it("resetWorkflow returns to the initial state", () => {
    state().addNode(node("a"));
    state().resetWorkflow();
    expect(Object.keys(state().nodesMap)).toHaveLength(0);
    expect(state().hasUnsavedChanges).toBe(false);
  });

  it("tidyUpWorkflow relayouts nodes", () => {
    state().addNode(node("a"));
    state().tidyUpWorkflow();
    expect(state().nodesMap.a).toBeDefined();
    expect(state().hasUnsavedChanges).toBe(true);
  });

  it("getNodeById and getEdgeById read from the maps", () => {
    state().addNode(node("a"));
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "a", targetHandle: "t" },
    );
    expect(state().getNodeById("a")?.id).toBe("a");
    expect(state().getEdgeById("xy-edge__a-a")?.source).toBe("a");
  });
});

describe("editor and execution mode", () => {
  it("sets editor mode, execution mode and next execution id", () => {
    state().setEditorMode("execution");
    expect(state().editorMode).toBe("execution");
    state().setExecutionMode(2);
    expect(state().executionMode).toBe(2);
    state().setNextExecutionId("exec-9");
    expect(state().nextExecutionId).toBe("exec-9");
  });

  it("sets listening state with a node id", () => {
    state().setIsListening(true, "n1");
    expect(state().isListening).toBe(true);
    expect(state().listeningNodeId).toBe("n1");
  });

  it("sets and reads the last successful execution data", () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    state().setLastSuccessfulExecutionData({ data: {} } as any);
    expect(state().lastSuccessfulExecutionData).toBeDefined();
  });
});

describe("step execution animation", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("animates reachable nodes then syncs the final sets", () => {
    state().addNode(node("a"));
    state().addNode(node("b"));
    state().createEdge(
      { source: "a", sourceHandle: "s" },
      { target: "b", targetHandle: "t" },
    );
    state().setStepExecutionData({
      data: {
        nodeExecutions: [
          { nodeId: "a", status: "Success" },
          { nodeId: "b", status: "Success" },
        ],
        items: [],
      },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);
    expect(state().stepExecutionReachableNodeIds).toBeInstanceOf(Set);
    // run all cascading intervals to completion
    vi.runAllTimers();
    expect(state().stepExecutionReachableNodeIds?.size).toBeGreaterThan(0);
  });

  it("clearStepExecutionData resets the highlight sets", () => {
    state().setStepExecutionData({
      data: { nodeExecutions: [], items: [] },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any);
    vi.runAllTimers();
    state().clearStepExecutionData();
    expect(state().stepExecutionReachableNodeIds).toBeNull();
    expect(state().executedItems).toHaveLength(0);
  });
});

describe("per-node handles (SPEC5)", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const ifNode = (id: string, extra: Record<string, unknown> = {}): any =>
    node(id, { category: "logic", type: "if", version: "v1", handle: null, ...extra });
  const IF_KEYS = ["if-true", "if-false"];

  it("H1/C6: upgrades nodes on load without marking the workflow changed", () => {
    state().setWorkflow({ itemId: "w1", name: "wf", nodes: [ifNode("a")], edges: [] } as never);
    const loaded = state().nodesMap.a;
    expect(loaded.version).toBe("v2");
    expect(loaded.handle?.sources.map((e) => e.key)).toEqual(IF_KEYS);
    expect(state().hasUnsavedChanges).toBe(false);
  });

  it("C4: keeps every existing edge after the upgrade", () => {
    const edges = [
      { id: "e1", source: "a", target: "b", sourceHandle: "if-true", targetHandle: "target" },
      { id: "e2", source: "a", target: "c", sourceHandle: "if-false", targetHandle: "target" },
    ];
    state().setWorkflow({ itemId: "w1", nodes: [ifNode("a")], edges } as never);
    expect(Object.values(state().edgesMap)).toEqual(edges);
  });

  it("C11/H10: duplicate and paste give the copy its own handle", () => {
    state().setWorkflow({ itemId: "w1", nodes: [ifNode("a", { selected: true })], edges: [] } as never);
    state().duplicateNode("a");
    const copy = Object.values(state().nodesMap).find((n) => n.id !== "a")!;
    expect(copy.handle).toEqual(state().nodesMap.a.handle);
    expect(copy.handle).not.toBe(state().nodesMap.a.handle);

    state().copyNode("a");
    state().pasteNodes();
    const pasted = Object.values(state().nodesMap).filter((n) => n.id !== "a" && n.id !== copy.id);
    expect(pasted).toHaveLength(1);
    expect(pasted[0].handle).toEqual(state().nodesMap.a.handle);
    expect(pasted[0].handle!.sources).not.toBe(state().nodesMap.a.handle!.sources);
  });

  describe("updateNodeParameters", () => {
    it("C15: for a node without deriveHandle, behaves like updateNode and keeps handle and edges", () => {
      state().setWorkflow({
        itemId: "w1",
        nodes: [ifNode("a"), node("b")],
        edges: [{ id: "e1", source: "a", target: "b", sourceHandle: "if-true", targetHandle: "target" }],
      } as never);
      const handleBefore = state().nodesMap.a.handle;
      state().selectNode(state().nodesMap.a);

      state().updateNodeParameters("a", { conditions: [1] });

      expect(state().nodesMap.a.parameters).toEqual({ conditions: [1] });
      expect(state().nodesMap.a.handle).toBe(handleBefore);
      expect(state().selectedNode?.parameters).toEqual({ conditions: [1] });
      expect(Object.keys(state().edgesMap)).toEqual(["e1"]);
      expect(state().hasUnsavedChanges).toBe(true);
    });

    describe("C15: a node whose definition derives its handle", () => {
      const switchLike: WorkflowNodeDefinition = {
        ...NodeDefinitions.find((d) => d.type === "code")!,
        id: "logic-setfield-v1",
        category: "logic",
        type: "setfield",
        deriveHandle: (parameters) => ({
          sources: ((parameters?.outputs as string[]) ?? []).map((key) => ({ key, label: "" })),
          targets: ((parameters?.inputs as string[]) ?? []).map((key) => ({ key, label: "" })),
        }),
      };
      beforeEach(() => {
        NodeDefinitions.push(switchLike);
      });
      afterEach(() => {
        NodeDefinitions.splice(NodeDefinitions.indexOf(switchLike), 1);
      });

      it("rebuilds the handle and removes only that node's edges on vanished keys", () => {
        state().setWorkflow({
          itemId: "w1",
          nodes: [
            node("s", { category: "logic", type: "setfield", version: "v2", parameters: { outputs: ["o1", "o2"], inputs: ["i1", "i2"] } }),
            ifNode("other"),
            node("x"),
          ],
          edges: [
            { id: "keep-o1", source: "s", target: "x", sourceHandle: "o1", targetHandle: "target" },
            { id: "drop-o2", source: "s", target: "x", sourceHandle: "o2", targetHandle: "target" },
            { id: "keep-i1", source: "x", target: "s", sourceHandle: "source", targetHandle: "i1" },
            { id: "drop-i2", source: "x", target: "s", sourceHandle: "source", targetHandle: "i2" },
            { id: "keep-other-o2", source: "other", target: "x", sourceHandle: "o2", targetHandle: "i2" },
          ],
        } as never);

        state().updateNodeParameters("s", { outputs: ["o1"], inputs: ["i1"] });

        expect(state().nodesMap.s.handle).toEqual({
          sources: [{ key: "o1", label: "" }],
          targets: [{ key: "i1", label: "" }],
        });
        expect(Object.keys(state().edgesMap).sort()).toEqual(["keep-i1", "keep-o1", "keep-other-o2"]);
        expect(state().nodesMap.other).toBeDefined();
        expect(state().hasUnsavedChanges).toBe(true);
      });
    });

    it("ignores an unknown node", () => {
      state().updateNodeParameters("missing", { a: 1 });
      expect(state().hasUnsavedChanges).toBe(false);
    });
  });
});

describe("Switch node in the store (SPEC6)", () => {
  const A = "switch-handle-source-aaaaa1";
  const B = "switch-handle-source-bbbbb2";
  const FALLBACK = "switch-handle-source-fallback";
  const rule = (handleKey: string, handleLabel = "") => ({ handleKey, handleLabel, conditionType: "all", conditions: [] });
  const options = (fallbackOutput = "none") => ({ fallbackOutput, renameFallbackOutput: "", ignoreCase: false, allMatchingOutputs: false });
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const switchNode = (parameters: Record<string, unknown>, extra: Record<string, unknown> = {}): any =>
    node("s", { category: "logic", type: "switch", version: "v2", parameters, handle: null, ...extra });
  const edge = (id: string, sourceHandle: string, source = "s") => ({ id, source, target: "x", sourceHandle, targetHandle: "target" });
  const load = (parameters: Record<string, unknown>, edges: unknown[] = [], extra: Record<string, unknown> = {}) =>
    state().setWorkflow({ itemId: "w1", nodes: [switchNode(parameters, extra), node("x"), node("other")], edges } as never);
  const sourceKeys = () => state().nodesMap.s.handle!.sources.map((e) => e.key);

  it("C10: on load re-derives the handle from the parameters, whatever is stored, without marking changed", () => {
    load({ mode: "rules", rules: [rule(A), rule(B)], options: options() }, [edge("e1", A)], {
      handle: { sources: [{ key: "source", label: "" }], targets: [] },
    });
    expect(sourceKeys()).toEqual([A, B]);
    expect(state().nodesMap.s.handle!.targets).toEqual([{ key: "target", label: "" }]);
    expect(state().hasUnsavedChanges).toBe(false);
    expect(Object.keys(state().edgesMap)).toEqual(["e1"]);
  });

  it("H2: adding a rule adds its handle at the bottom", () => {
    load({ mode: "rules", rules: [rule(A)], options: options() });
    state().updateNodeParameters("s", { ...state().nodesMap.s.parameters, rules: [rule(A), rule(B)] });
    expect(sourceKeys()).toEqual([A, B]);
  });

  it("H3/H4: renaming and reordering keep keys and edges", () => {
    load({ mode: "rules", rules: [rule(A), rule(B)], options: options() }, [edge("e1", A), edge("e2", B)]);
    state().updateNodeParameters("s", { ...state().nodesMap.s.parameters, rules: [rule(B, "Big"), rule(A, "Paid")] });
    expect(state().nodesMap.s.handle!.sources).toEqual([
      { key: B, label: "Big" },
      { key: A, label: "Paid" },
    ]);
    expect(Object.keys(state().edgesMap).sort()).toEqual(["e1", "e2"]);
  });

  it("C1: deleting a rule removes only that node's edges on that key", () => {
    load({ mode: "rules", rules: [rule(A), rule(B)], options: options() }, [edge("e1", A), edge("e2", B), edge("e3", B, "other")]);
    state().updateNodeParameters("s", { ...state().nodesMap.s.parameters, rules: [rule(A)] });
    expect(Object.keys(state().edgesMap).sort()).toEqual(["e1", "e3"]);
  });

  it("C3: turning Extra output off removes the fallback edges", () => {
    load({ mode: "rules", rules: [rule(A)], options: options("extra") }, [edge("e1", A), edge("f", FALLBACK)]);
    expect(sourceKeys()).toEqual([A, FALLBACK]);
    state().updateNodeParameters("s", { ...state().nodesMap.s.parameters, options: options("none") });
    expect(Object.keys(state().edgesMap)).toEqual(["e1"]);
  });

  it("H19: duplicate and paste keep keys and handle; the original keeps its edges", () => {
    load({ mode: "rules", rules: [rule(A, "Paid")], options: options() }, [edge("e1", A)]);
    state().duplicateNode("s");
    const copy = Object.values(state().nodesMap).find((n) => n.type === "switch" && n.id !== "s")!;
    expect(copy.handle).toEqual(state().nodesMap.s.handle);
    expect(copy.parameters).toEqual(state().nodesMap.s.parameters);
    expect(Object.values(state().edgesMap).every((e) => e.source === "s")).toBe(true);
  });
});

describe("saving and reloading", () => {
  const load = (nodes: unknown[]) => state().setWorkflow({ itemId: "w1", nodes, edges: [] } as never);

  it("markSaved clears the unsaved flag when nothing changed since the save was sent", () => {
    load([node("a")]);
    state().updateNode("a", { name: "Renamed" });
    const { nodesMap, edgesMap } = state();
    state().markSaved(nodesMap, edgesMap);
    expect(state().hasUnsavedChanges).toBe(false);
  });

  it("markSaved keeps the flag when an edit landed while the save was in flight", () => {
    load([node("a")]);
    state().updateNode("a", { name: "First" });
    const { nodesMap, edgesMap } = state();
    state().updateNode("a", { name: "Second" });
    state().markSaved(nodesMap, edgesMap);
    expect(state().hasUnsavedChanges).toBe(true);
  });

  it("a reload points the open inspector at the reloaded copy of its node, still selected", () => {
    load([node("a", { name: "Old" })]);
    state().selectNode(state().nodesMap.a);
    load([node("a", { name: "From server" })]);
    expect(state().selectedNode?.name).toBe("From server");
    expect(state().selectedNode).toBe(state().nodesMap.a);
    expect(state().nodesMap.a.selected).toBe(true);
  });

  it("a reload without the selected node clears the selection", () => {
    load([node("a")]);
    state().selectNode(state().nodesMap.a);
    load([node("b")]);
    expect(state().selectedNode).toBeNull();
  });
});
