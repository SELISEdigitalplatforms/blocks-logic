import { createStore } from "zustand/vanilla";
import {
  OnNodesChange,
  OnEdgesChange,
  OnConnect,
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
  Connection,
  Edge,
} from "@xyflow/react";
import { v4 as uuidv4 } from "uuid";
import { ExecutedItem, ExecutedNode, Workflow } from "../models/workflow.model";
import { EditorNode } from "@blocks-workflow/models/node.model";
import { getLayoutedElements } from "../utils/layout-utils";

// interface  ExtendNode extends Node, WorkflowNode {}

export type WorkflowState = {
  nodesMap: Record<string, EditorNode>;
  edgesMap: Record<string, Edge>;

  workflowId: string | null;
  workflowName: string;
  isPublished: boolean;
  isDirty: boolean;
  executedItems: ExecutedItem[];

  executedNodes: ExecutedNode[];

  selectedNode: EditorNode | null;
  selectedHandle: string | null;

  copiedNodes: EditorNode[];
  copiedEdges: Edge[];

  isConfigModalOpen: boolean;
  isPanelOpen: boolean;

  // React Flow handlers
  onNodesChange: OnNodesChange;
  onEdgesChange: OnEdgesChange;
  onConnect: OnConnect;

  // Node operations
  addNode: (node: EditorNode) => void;
  updateNode: (nodeId: string, updates: Partial<EditorNode>) => void;
  deleteNode: (nodeId: string) => void;
  duplicateNode: (nodeId: string) => void;
  copyNode: (nodeId: string) => void;
  copySelectedNodes: () => void;
  pasteNodes: (position?: { x: number; y: number }) => void;
  selectNode: (node: EditorNode) => void;
  deselectNode: () => void;

  // Handle operations
  selectHandle: (handle: string) => void;
  deselectHandle: () => void;

  // Edge operations
  createEdge: (
    sourceNode: { source: string; sourceHandle: string },
    targetNode: { target: string; targetHandle: string },
  ) => void;
  deleteEdge: (edgeId: string) => void;

  // Selection operations

  openConfigModal: () => void;
  closeConfigModal: () => void;

  // Panel operations
  openNodeLibraryPanel: () => void;
  closeNodeLibraryPanel: () => void;

  // Workflow operations
  setWorkflow: (workflow: Workflow) => void;
  resetWorkflow: () => void;
  tidyUpWorkflow: () => void;

  // Utility methods
  getNodeById: (nodeId: string) => EditorNode | undefined;
  getEdgeById: (edgeId: string) => Edge | undefined;
};

export const createWorkflowStore = () => createStore<WorkflowState>((set, get) => ({
  // Initial state
  nodesMap: {},
  edgesMap: {},
  nodeOutputSchemas: {},
  nodeIdCounter: 1,
  selectedNode: null,
  selectedHandle: null,
  copiedNodes: [],
  copiedEdges: [],
  isConfigModalOpen: false,
  isPanelOpen: false,
  workflowId: null,
  workflowName: "",
  isPublished: false,
  isDirty: false,
  executedItems: [],
  executedNodes: [],

  // React Flow handlers
  onNodesChange: (changes) => {
    const currentNodes = Object.values(get().nodesMap);
    const updatedNodes = applyNodeChanges(changes, currentNodes);
    const nodesMap = updatedNodes.reduce(
      (acc, node) => {
        acc[node.id] = node as EditorNode;
        return acc;
      },
      {} as Record<string, EditorNode>,
    );
    const shouldDirty = changes.some((change) =>
      ["position", "remove", "add", "replace"].includes(change.type),
    );
    const stateUpdate: Partial<WorkflowState> = { nodesMap };
    
    if (shouldDirty) {
      stateUpdate.isDirty = true;
    }

    const selectedNodeId = get().selectedNode?.id;
    if (selectedNodeId) {
      const isSelectedNodeDeselected = changes.some(
        (change) => change.type === "select" && !change.selected && change.id === selectedNodeId
      );
      if (isSelectedNodeDeselected) {
        stateUpdate.selectedNode = null;
        stateUpdate.isConfigModalOpen = false;
      }
    }

    // Auto-select edges between selected nodes
    const selectionChanged = changes.some((change) => change.type === "select");
    if (selectionChanged) {
      const selectedNodeIds = new Set(updatedNodes.filter((n) => n.selected).map((n) => n.id));
      const currentEdges = Object.values(get().edgesMap);
      let edgesChanged = false;
      const newEdgesMap = { ...get().edgesMap };

      for (const edge of currentEdges) {
        const bothNodesSelected =
          selectedNodeIds.has(edge.source) && selectedNodeIds.has(edge.target);

        if (bothNodesSelected && !edge.selected) {
          newEdgesMap[edge.id] = { ...edge, selected: true };
          edgesChanged = true;
        } else if (!bothNodesSelected && edge.selected) {
          const edgeNodesChanged = changes.some(
            (change) =>
              change.type === "select" &&
              (change.id === edge.source || change.id === edge.target)
          );
          if (edgeNodesChanged) {
            newEdgesMap[edge.id] = { ...edge, selected: false };
            edgesChanged = true;
          }
        }
      }

      if (edgesChanged) {
        stateUpdate.edgesMap = newEdgesMap;
      }
    }

    set(stateUpdate);
  },

  onEdgesChange: (changes) => {
    const currentEdges = Object.values(get().edgesMap);
    const updatedEdges = applyEdgeChanges(changes, currentEdges);
    const edgesMap = updatedEdges.reduce(
      (acc, edge) => {
        acc[edge.id] = edge;
        return acc;
      },
      {} as Record<string, Edge>,
    );
    const shouldDirty = changes.some((change) =>
      ["remove", "add", "replace"].includes(change.type),
    );
    set({
      edgesMap,
      ...(shouldDirty && { isDirty: true }),
    });
  },

  onConnect: (connection: Connection) => {
    const currentEdges = Object.values(get().edgesMap);
    const updatedEdges = addEdge(connection, currentEdges);
    const edgesMap = updatedEdges.reduce(
      (acc, edge) => {
        acc[edge.id] = edge;
        return acc;
      },
      {} as Record<string, Edge>,
    );
    set({
      edgesMap,
      isDirty: true,
    });
  },

  // Node operations
  addNode: (node) => {
    const { nodesMap } = get();
    // Enforce unique node names
    const existingNames = new Set(Object.values(nodesMap).map((n) => n.name));
    let uniqueName = node.name;
    if (existingNames.has(uniqueName)) {
      let counter = 1;
      while (existingNames.has(`${node.name} ${counter}`)) counter++;
      uniqueName = `${node.name} ${counter}`;
    }
    const nodeWithUniqueName = { ...node, name: uniqueName };
    set({
      nodesMap: { ...nodesMap, [node.id]: nodeWithUniqueName },
      isDirty: true,
    });
  },

  updateNode: (nodeId: string, updates: Partial<EditorNode>) => {
    const { nodesMap, selectedNode } = get();
    const node = nodesMap[nodeId];
    if (!node) return;
    const updatedNode = { ...node, ...updates };
    set({
      nodesMap: { ...nodesMap, [nodeId]: updatedNode },
      selectedNode: selectedNode?.id === nodeId ? updatedNode : selectedNode,
      isDirty: true,
    });
  },

  deleteNode: (nodeId: string) => {
    const { nodesMap, edgesMap } = get();
    const { [nodeId]: _, ...remainingNodes } = nodesMap;

    // Remove edges connected to this node
    const remainingEdges = Object.fromEntries(
      Object.entries(edgesMap).filter(
        ([_, edge]) => edge.source !== nodeId && edge.target !== nodeId,
      ),
    );

    set({
      nodesMap: remainingNodes,
      edgesMap: remainingEdges,
      isDirty: true,
    });
  },

  duplicateNode: (nodeId: string) => {
    const { nodesMap } = get();
    const node = nodesMap[nodeId];
    if (!node) return;

    const newId = uuidv4().replace(/-/g, "");
    // Enforce unique name for duplicated node
    const existingNames = new Set(Object.values(nodesMap).map((n) => n.name));
    let uniqueName = node.name;
    let counter = 1;
    while (existingNames.has(uniqueName)) {
      uniqueName = `${node.name} ${counter}`;
      counter++;
    }
    const newNode: EditorNode = {
      ...node,
      id: newId,
      name: uniqueName,
      selected: false,
      position: {
        x: node.position.x + 50,
        y: node.position.y + 100,
      },
    };

    set({
      nodesMap: { ...nodesMap, [newId]: newNode },
      isDirty: true,
    });
  },

  copyNode: (nodeId: string) => {
    const { nodesMap } = get();
    const node = nodesMap[nodeId];
    if (node) {
      set({ copiedNodes: [node], copiedEdges: [] });
    }
  },

  copySelectedNodes: () => {
    const { nodesMap, edgesMap } = get();
    const selectedNodes = Object.values(nodesMap).filter((node) => node.selected);
    if (selectedNodes.length > 0) {
      const selectedNodeIds = new Set(selectedNodes.map((n) => n.id));
      const copiedEdges = Object.values(edgesMap).filter(
        (edge) => selectedNodeIds.has(edge.source) && selectedNodeIds.has(edge.target),
      );
      set({ copiedNodes: selectedNodes, copiedEdges });
    }
  },

  pasteNodes: (position?: { x: number; y: number }) => {
    const { copiedNodes, copiedEdges, nodesMap, edgesMap } = get();
    if (!copiedNodes || copiedNodes.length === 0) return;

    let newNodesMap = { ...nodesMap };
    let newEdgesMap = { ...edgesMap };
    let existingNames = new Set(Object.values(newNodesMap).map((n) => n.name));

    const newPastedNodeIds = new Set<string>();
    const oldToNewId: Record<string, string> = {};

    copiedNodes.forEach((copiedNode) => {
      const newId = uuidv4().replace(/-/g, "");
      oldToNewId[copiedNode.id] = newId;
      // Enforce unique name for pasted node
      let uniqueName = copiedNode.name;
      let counter = 1;
      while (existingNames.has(uniqueName)) {
        uniqueName = `${copiedNode.name} ${counter}`;
        counter++;
      }
      existingNames.add(uniqueName);

      const newNode: EditorNode = {
        ...copiedNode,
        id: newId,
        name: uniqueName,
        selected: true,
        position: position || {
          x: copiedNode.position.x + 50,
          y: copiedNode.position.y + 100,
        },
      };
      newNodesMap[newId] = newNode;
      newPastedNodeIds.add(newId);
    });

    if (copiedEdges && copiedEdges.length > 0) {
      copiedEdges.forEach((edge) => {
        const newSource = oldToNewId[edge.source];
        const newTarget = oldToNewId[edge.target];
        if (newSource && newTarget) {
          const newEdgeId = `xy-edge__${newSource}-${newTarget}`;
          newEdgesMap[newEdgeId] = {
            ...edge,
            id: newEdgeId,
            source: newSource,
            target: newTarget,
            selected: true,
          };
        }
      });
    }

    // Deselect existing nodes
    Object.keys(newNodesMap).forEach((id) => {
      if (!newPastedNodeIds.has(id)) {
         newNodesMap[id] = { ...newNodesMap[id], selected: false };
      }
    });
    // Deselect existing edges
    Object.keys(newEdgesMap).forEach((id) => {
      if (edgesMap[id]) {
        newEdgesMap[id] = { ...newEdgesMap[id], selected: false };
      }
    });

    set({
      nodesMap: newNodesMap,
      edgesMap: newEdgesMap,
      isDirty: true,
    });
  },

  createEdge: (
    sourceNode: { source: string; sourceHandle: string },
    targetNode: { target: string; targetHandle: string },
  ) => {
    const { edgesMap } = get();
    const newEdge: Edge = {
      id: `xy-edge__${sourceNode.source}-${targetNode.target}`,
      source: sourceNode.source,
      sourceHandle: sourceNode.sourceHandle,
      target: targetNode.target,
      targetHandle: targetNode.targetHandle,
    };

    set({
      edgesMap: { ...edgesMap, [newEdge.id]: newEdge },
      isDirty: true,
    });
  },

  // Edge operations
  deleteEdge: (edgeId: string) => {
    const { edgesMap } = get();
    const { [edgeId]: _, ...remainingEdges } = edgesMap;

    set({
      edgesMap: remainingEdges,
      isDirty: true,
    });
  },

  // Selection operations
  selectNode: (node: EditorNode | null) => {
    set({ selectedNode: node });
  },

  deselectNode: () => {
    set({ selectedNode: null });
  },

  selectHandle: (handle: string) => {
    set({ selectedHandle: handle });
  },

  deselectHandle: () => {
    set({ selectedHandle: null });
  },

  openConfigModal: () => {
    set({ isConfigModalOpen: true });
  },

  closeConfigModal: () => {
    set({ isConfigModalOpen: false });
  },

  // Panel operations
  openNodeLibraryPanel: () => {
    set({ isPanelOpen: true });
  },

  closeNodeLibraryPanel: () => {
    set({ isPanelOpen: false });
    set({ selectedHandle: null });
  },

  // Workflow operations
  setWorkflow: (workflow) => {
    const nodesMap = workflow.nodes
      ? workflow.nodes.reduce(
          (acc, node) => {
            acc[node.id] = node as EditorNode;
            return acc;
          },
          {} as Record<string, EditorNode>,
        )
      : {};

    const edgesMap = workflow.edges
      ? workflow.edges.reduce(
          (acc, edge) => {
            acc[edge.id] = edge;
            return acc;
          },
          {} as Record<string, Edge>,
        )
      : {};

    const executedItems = workflow.items ?? [];
    const executedNodes = workflow.nodeExecutions ?? [];

    set({
      nodesMap,
      edgesMap,
      workflowId: workflow.itemId || null,
      workflowName: workflow.name || "",
      isPublished: workflow.isPublished || false,
      isDirty: false,
      executedItems,
      executedNodes,
    });
  },

  // setWorkflowActive: (isActive: boolean) => {
  //   set({ isActive, isDirty: true });
  // },

  resetWorkflow: () => {
    set({
      nodesMap: {},
      edgesMap: {},
      selectedNode: null,
      isConfigModalOpen: false,
      isPanelOpen: false,
      workflowId: null,
      workflowName: "",
      isPublished: false,
      isDirty: false,
      executedItems: [],
      executedNodes: [],
    });
  },

  tidyUpWorkflow: () => {
    const { nodesMap, edgesMap } = get();
    const newNodesMap = getLayoutedElements(nodesMap, edgesMap);
    set({
      nodesMap: newNodesMap,
      isDirty: true,
    });
  },

  // Utility methods
  getNodeById: (nodeId: string) => {
    return get().nodesMap[nodeId];
  },

  getEdgeById: (edgeId: string) => {
    return get().edgesMap[edgeId];
  },
}));

export type WorkflowStore = ReturnType<typeof createWorkflowStore>;
