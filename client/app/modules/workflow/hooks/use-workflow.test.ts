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

// ─── Mock store state & actions ───────────────────────────────────────────────
const mockSelectNode = vi.fn();
const mockOpenConfigModal = vi.fn();
const mockSetWorkflow = vi.fn();
const mockGetNodeById = vi.fn();
const mockGetEdgeById = vi.fn();
const mockResetWorkflow = vi.fn();
const mockSetWorkflowActive = vi.fn();

const mockEdge1 = mockWorkflowEdge1 as unknown as Edge;
const mockEditorNode1 = mockWorkflowNode1 as unknown as EditorNode;
const mockEditorNode2 = mockWorkflowNode2 as unknown as EditorNode;

const buildStoreState = (overrides: Record<string, unknown> = {}) => ({
  nodesMap: {
    [mockEditorNode1.id]: mockEditorNode1,
    [mockEditorNode2.id]: mockEditorNode2,
  },
  edgesMap: { [mockEdge1.id]: mockEdge1 },
  selectedNode: null,
  isConfigModalOpen: false,
  isPanelOpen: false,
  workflowId: mockWorkflow1.itemId,
  workflowName: mockWorkflow1.name,
  isActive: mockWorkflow1.isActive,
  isDirty: false,
  onNodesChange: vi.fn(),
  onEdgesChange: vi.fn(),
  onConnect: vi.fn(),
  addNode: vi.fn(),
  updateNode: vi.fn(),
  deleteNode: vi.fn(),
  duplicateNode: vi.fn(),
  createEdge: vi.fn(),
  deleteEdge: vi.fn(),
  selectNode: mockSelectNode,
  deselectNode: vi.fn(),
  openConfigModal: mockOpenConfigModal,
  closeConfigModal: vi.fn(),
  openNodeLibraryPanel: vi.fn(),
  closeNodeLibraryPanel: vi.fn(),
  setWorkflow: mockSetWorkflow,
  setWorkflowActive: mockSetWorkflowActive,
  resetWorkflow: mockResetWorkflow,
  getNodeById: mockGetNodeById,
  getEdgeById: mockGetEdgeById,
  ...overrides,
});

const { mockUseWorkflowStore } = vi.hoisted(() => {
  const mockUseWorkflowStore = vi.fn();
  return { mockUseWorkflowStore };
});

vi.mock("../store/workflow-store", () => ({
  useWorkflowStore: mockUseWorkflowStore,
}));

// ─── Mock useReactFlow ────────────────────────────────────────────────────────
const mockSetCenter = vi.fn();
const mockFitView = vi.fn();

vi.mock("@xyflow/react", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    useReactFlow: () => ({
      setCenter: mockSetCenter,
      fitView: mockFitView,
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

    it("should expose workflowId, workflowName and isActive from the store", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.workflowId).toBe(mockWorkflow1.itemId);
      expect(result.current.workflowName).toBe(mockWorkflow1.name);
      expect(result.current.isActive).toBe(mockWorkflow1.isActive);
    });

    it("should expose isDirty from the store", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.isDirty).toBe(false);
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

  describe("exportWorkflow", () => {
    it("should return an object containing workflow state and metadata", () => {
      const { result } = renderHook(() => useWorkflow());
      const exported = result.current.exportWorkflow();

      expect(exported.id).toBe(mockWorkflow1.itemId);
      expect(exported.name).toBe(mockWorkflow1.name);
      expect(exported.isActive).toBe(mockWorkflow1.isActive);
      expect(exported.nodes).toEqual([mockEditorNode1, mockEditorNode2]);
      expect(exported.edges).toEqual([mockEdge1]);
      expect(exported.metadata.version).toBe("1.0");
      expect(exported.metadata.exportedAt).toBeDefined();
    });
  });

  describe("importWorkflow", () => {
    it("should call setWorkflow with the provided workflow data", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.importWorkflow(mockWorkflow1);
      });

      expect(mockSetWorkflow).toHaveBeenCalledWith(mockWorkflow1);
    });
  });

  describe("getWorkflowStats", () => {
    it("should return correct node and edge counts", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.getWorkflowStats.totalNodes).toBe(2);
      expect(result.current.getWorkflowStats.totalEdges).toBe(1);
    });

    it("should reflect hasUnsavedChanges from isDirty", () => {
      const { result } = renderHook(() => useWorkflow());
      expect(result.current.getWorkflowStats.hasUnsavedChanges).toBe(false);
    });
  });

  describe("centerOnNode", () => {
    it("should call reactFlowInstance.setCenter with node position", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.centerOnNode(mockEditorNode1.id);
      });

      expect(mockSetCenter).toHaveBeenCalledWith(
        mockEditorNode1.position.x,
        mockEditorNode1.position.y,
        expect.objectContaining({ zoom: 1.5 }),
      );
    });

    it("should do nothing when node is not found", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.centerOnNode("nonexistent-id");
      });

      expect(mockSetCenter).not.toHaveBeenCalled();
    });
  });

  describe("fitView", () => {
    it("should call reactFlowInstance.fitView", () => {
      const { result } = renderHook(() => useWorkflow());

      act(() => {
        result.current.fitView();
      });

      expect(mockFitView).toHaveBeenCalledWith(expect.objectContaining({ padding: 0.2 }));
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
});
