import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useWorkflow } from "./use-workflow";
import {
  mockWorkflowNode1,
  mockWorkflowNode2,
  mockWorkflowEdge1,
  mockWorkflow1,
} from "../test-utils/__mocks__";
import type { EditorNode } from "@blocks-workflow/models/node.model";
import type { Edge } from "@xyflow/react";

// ─── Mock store actions ───────────────────────────────────────────────────────
const mockSelectNode = vi.fn();
const mockOpenConfigModal = vi.fn();
const mockSetWorkflow = vi.fn();
const mockGetNodeById = vi.fn();
const mockGetEdgeById = vi.fn();

const mockEdge1 = mockWorkflowEdge1 as unknown as Edge;
const mockEditorNode1 = mockWorkflowNode1 as unknown as EditorNode;
const mockEditorNode2 = mockWorkflowNode2 as unknown as EditorNode;

// A full store state matching the fields useWorkflow selects. Actions default
// to spies; the few asserted below are shared references declared above.
const buildStoreState = (overrides: Record<string, unknown> = {}) => ({
  nodesMap: {
    [mockEditorNode1.id]: mockEditorNode1,
    [mockEditorNode2.id]: mockEditorNode2,
  },
  edgesMap: { [mockEdge1.id]: mockEdge1 },
  selectedNode: null,
  selectedHandle: null,
  isConfigModalOpen: false,
  isPanelOpen: false,
  workflowId: mockWorkflow1.itemId,
  workflowName: mockWorkflow1.name,
  hasUnsavedChanges: false,
  editorMode: "edit",
  executionMode: "idle",
  lastSuccessfulExecutionData: null,
  onNodesChange: vi.fn(),
  onEdgesChange: vi.fn(),
  onConnect: vi.fn(),
  addNode: vi.fn(),
  updateNode: vi.fn(),
  deleteNode: vi.fn(),
  duplicateNode: vi.fn(),
  copyNode: vi.fn(),
  copySelectedNodes: vi.fn(),
  pasteNodes: vi.fn(),
  createEdge: vi.fn(),
  deleteEdge: vi.fn(),
  selectNode: mockSelectNode,
  deselectNode: vi.fn(),
  deselectAllEdges: vi.fn(),
  selectHandle: vi.fn(),
  deselectHandle: vi.fn(),
  openConfigModal: mockOpenConfigModal,
  closeConfigModal: vi.fn(),
  openNodeLibraryPanel: vi.fn(),
  closeNodeLibraryPanel: vi.fn(),
  setWorkflow: mockSetWorkflow,
  setEditorMode: vi.fn(),
  setExecutionMode: vi.fn(),
  resetWorkflow: vi.fn(),
  tidyUpWorkflow: vi.fn(),
  setLastSuccessfulExecutionData: vi.fn(),
  getNodeById: mockGetNodeById,
  getEdgeById: mockGetEdgeById,
  executedItems: [],
  executedNodes: [],
  stepExecutionTraversedEdgeIds: [],
  stepExecutionReachableNodeIds: [],
  setStepExecutionData: vi.fn(),
  isListening: false,
  listeningNodeId: null,
  setIsListening: vi.fn(),
  nextExecutionId: null,
  setNextExecutionId: vi.fn(),
  ...overrides,
});

const { mockUseWorkflowStore } = vi.hoisted(() => {
  const mockUseWorkflowStore = vi.fn();
  return { mockUseWorkflowStore };
});

vi.mock("../store", () => ({
  useWorkflowStore: mockUseWorkflowStore,
}));

// ─── Mock useReactFlow ────────────────────────────────────────────────────────
const mockFitView = vi.fn();
const mockZoomIn = vi.fn();
const mockZoomOut = vi.fn();

vi.mock("@xyflow/react", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    useReactFlow: () => ({
      fitView: mockFitView,
      zoomIn: mockZoomIn,
      zoomOut: mockZoomOut,
    }),
  };
});

describe("useWorkflow", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    const state = buildStoreState();
    // useWorkflowStore is called as a selector: (state) => state.someField
    mockUseWorkflowStore.mockImplementation((selector: (s: typeof state) => unknown) =>
      selector(state),
    );
    mockGetNodeById.mockImplementation((id: string) =>
      id === mockEditorNode1.id ? mockEditorNode1 : undefined,
    );
  });

  describe("derived state", () => {
    it("should return nodes array computed from nodesMap", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.nodes).toEqual([mockEditorNode1, mockEditorNode2]);
    });

    it("should return edges array computed from edgesMap", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.edges).toEqual([mockEdge1]);
    });

    it("should expose workflowId, workflowName and hasUnsavedChanges from the store", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.workflowId).toBe(mockWorkflow1.itemId);
      expect(result.current.workflowName).toBe(mockWorkflow1.name);
      expect(result.current.hasUnsavedChanges).toBe(false);
    });
  });

  describe("selectAndConfigureNode", () => {
    it("should call selectNode and openConfigModal", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.selectAndConfigureNode(mockEditorNode1);
      });

      expect(mockSelectNode).toHaveBeenCalledWith(mockEditorNode1);
      expect(mockOpenConfigModal).toHaveBeenCalled();
    });
  });

  describe("getNodeEdges", () => {
    it("should return incoming edges where target matches nodeId", () => {
      const { result } = renderHook(() => useWorkflow());
      const { incoming } = result.current.getNodeEdges(mockEditorNode2.id);
      expect(incoming).toEqual([mockEdge1]);
    });

    it("should return outgoing edges where source matches nodeId", () => {
      const { result } = renderHook(() => useWorkflow());
      const { outgoing } = result.current.getNodeEdges(mockEditorNode1.id);
      expect(outgoing).toEqual([mockEdge1]);
    });

    it("should return empty arrays when the node has no connections", () => {
      const { result } = renderHook(() => useWorkflow());
      const { incoming, outgoing } = result.current.getNodeEdges("nonexistent-id");
      expect(incoming).toEqual([]);
      expect(outgoing).toEqual([]);
    });
  });

  describe("getNodeNextSource", () => {
    it("should return the first unused source handle", () => {
      const { result } = renderHook(() => useWorkflow());
      // mockEdge1 uses sourceHandle "output" on node1, so "alt" is free.
      const next = result.current.getNodeNextSource(mockEditorNode1.id, ["output", "alt"]);
      expect(next).toBe("alt");
    });

    it("should fall back to the first handle when all are used", () => {
      const { result } = renderHook(() => useWorkflow());
      const next = result.current.getNodeNextSource(mockEditorNode1.id, ["output"]);
      expect(next).toBe("output");
    });
  });

  describe("getWorkflowStats", () => {
    it("should return correct node and edge counts", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.getWorkflowStats.totalNodes).toBe(2);
      expect(result.current.getWorkflowStats.totalEdges).toBe(1);
    });

    it("should reflect hasUnsavedChanges", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.getWorkflowStats.hasUnsavedChanges).toBe(false);
    });
  });

  describe("isNodeNameUnique", () => {
    it("should return false when another node already has the name", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.isNodeNameUnique(mockEditorNode2.name)).toBe(false);
    });

    it("should return true for a brand new name", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.isNodeNameUnique("A totally new node")).toBe(true);
    });

    it("should ignore the excluded node id", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(
        result.current.isNodeNameUnique(mockEditorNode1.name, mockEditorNode1.id),
      ).toBe(true);
    });
  });

  describe("isValidConnection", () => {
    it("should return false when source equals target (self-loop)", () => {
      const { result } = renderHook(() => useWorkflow());
      const connection = {
        source: mockEditorNode1.id,
        target: mockEditorNode1.id,
        sourceHandle: null,
        targetHandle: null,
      };
      expect(result.current.isValidConnection(connection)).toBe(false);
    });

    it("should return true when source and target are different nodes", () => {
      const { result } = renderHook(() => useWorkflow());
      const connection = {
        source: mockEditorNode1.id,
        target: mockEditorNode2.id,
        sourceHandle: null,
        targetHandle: null,
      };
      expect(result.current.isValidConnection(connection)).toBe(true);
    });
  });

  describe("onNodeClick", () => {
    it("should call selectAndConfigureNode when the node is found in the store", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.onNodeClick(
          {} as React.MouseEvent,
          {
            id: mockEditorNode1.id,
          } as never,
        );
      });

      expect(mockSelectNode).toHaveBeenCalledWith(mockEditorNode1);
      expect(mockOpenConfigModal).toHaveBeenCalled();
    });

    it("should do nothing when a modifier key is held", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.onNodeClick(
          { ctrlKey: true } as React.MouseEvent,
          { id: mockEditorNode1.id } as never,
        );
      });

      expect(mockSelectNode).not.toHaveBeenCalled();
    });

    it("should do nothing when the clicked node is not found in the store", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.onNodeClick(
          {} as React.MouseEvent,
          {
            id: "nonexistent-id",
          } as never,
        );
      });

      expect(mockSelectNode).not.toHaveBeenCalled();
    });
  });

  describe("react flow passthroughs", () => {
    it("should expose the react flow fitView method", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.fitView();
      });

      expect(mockFitView).toHaveBeenCalled();
    });
  });
});
