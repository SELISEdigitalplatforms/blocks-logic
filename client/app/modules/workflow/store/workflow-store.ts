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
import { IGetWorkflowExecutionByIdResponse } from "../types/workflow.service.type";
import { buildExecutedSubgraph } from "../utils/workflow-execution-editor.util";

// interface  ExtendNode extends Node, WorkflowNode {}

export type WorkflowState = {
  nodesMap: Record<string, EditorNode>;
  edgesMap: Record<string, Edge>;

  workflowId: string | null;
  workflowName: string;
  isPublished: boolean;
  hasUnsavedChanges: boolean;
  executedItems: ExecutedItem[];
  executedNodes: ExecutedNode[];

  stepExecutionReachableNodeIds: Set<string> | null;
  stepExecutionTraversedEdgeIds: Set<string> | null;
  
  lastSuccessfulExecutionData: IGetWorkflowExecutionByIdResponse | null;

  selectedNode: EditorNode | null;
  selectedHandle: string | null;

  copiedNodes: EditorNode[];
  copiedEdges: Edge[];

  isConfigModalOpen: boolean;
  isPanelOpen: boolean;

  // Listening state
  isListening: boolean;
  listeningNodeId: string | null;
  setIsListening: (isListening: boolean, nodeId?: string | null) => void;

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
  selectNode: (node: EditorNode | null) => void;
  deselectNode: () => void;
  deselectAllEdges: () => void;

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
  setStepExecutionData: (execution: IGetWorkflowExecutionByIdResponse) => void;
  clearStepExecutionData: () => void;
  setLastSuccessfulExecutionData: (data: IGetWorkflowExecutionByIdResponse | null) => void;

  // Utility methods
  getNodeById: (nodeId: string) => EditorNode | undefined;
  getEdgeById: (edgeId: string) => Edge | undefined;

  // Editor mode
  editorMode: "editor" | "execution" | "version";
  executionMode: number | null;
  setEditorMode: (mode: "editor" | "execution" | "version") => void;
  setExecutionMode: (mode: number | null) => void;
  nextExecutionId: string | null; 
  setNextExecutionId: (id: string | null) => void;
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
  hasUnsavedChanges: false,
  executedItems: [],
  executedNodes: [],
  stepExecutionReachableNodeIds: null,
  stepExecutionTraversedEdgeIds: null,
  lastSuccessfulExecutionData: null,
  editorMode: "editor",
  executionMode: null,
  isListening: false,
  listeningNodeId: null,
  nextExecutionId: null, 
  setNextExecutionId: (id) => set({ nextExecutionId: id }),

  setIsListening: (isListening, nodeId = null) => set({ isListening, listeningNodeId: nodeId }),

  setEditorMode: (mode) => set({ editorMode: mode }),
  setExecutionMode: (mode) => set({ executionMode: mode }),

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
      stateUpdate.hasUnsavedChanges = true;
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
      ...(shouldDirty && { hasUnsavedChanges: true }),
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
      hasUnsavedChanges: true,
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
      hasUnsavedChanges: true,
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
      hasUnsavedChanges: true,
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
      hasUnsavedChanges: true,
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
      hasUnsavedChanges: true,
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

    const newNodesMap = { ...nodesMap };
    const newEdgesMap = { ...edgesMap };
    const existingNames = new Set(Object.values(newNodesMap).map((n) => n.name));

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
      hasUnsavedChanges: true,
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
      hasUnsavedChanges: true,
    });
  },

  // Edge operations
  deleteEdge: (edgeId: string) => {
    const { edgesMap } = get();
    const { [edgeId]: _, ...remainingEdges } = edgesMap;

    set({
      edgesMap: remainingEdges,
      hasUnsavedChanges: true,
    });
  },

  // Selection operations
  selectNode: (node: EditorNode | null) => {
    const { nodesMap } = get();
    const updatedNodesMap = { ...nodesMap };
    
    Object.keys(updatedNodesMap).forEach((id) => {
      updatedNodesMap[id] = {
        ...updatedNodesMap[id],
        selected: node ? id === node.id : false,
      };
    });

    set({
      nodesMap: updatedNodesMap,
      selectedNode: node ? (updatedNodesMap[node.id] || node) : null,
    });
  },

  deselectNode: () => {
    const { nodesMap } = get();
    const updatedNodesMap = { ...nodesMap };
    
    Object.keys(updatedNodesMap).forEach((id) => {
      updatedNodesMap[id] = {
        ...updatedNodesMap[id],
        selected: false,
      };
    });

    set({
      nodesMap: updatedNodesMap,
      selectedNode: null,
    });
  },

  deselectAllEdges: () => {
    const { edgesMap } = get();
    const updatedEdgesMap = { ...edgesMap };
    
    Object.keys(updatedEdgesMap).forEach((id) => {
      updatedEdgesMap[id] = {
        ...updatedEdgesMap[id],
        selected: false,
      };
    });

    set({
      edgesMap: updatedEdgesMap,
    });
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

    // Preserve execution data to prevent it from disappearing after a save/refetch
    const executedItems = (workflow.items && workflow.items.length > 0) 
      ? workflow.items 
      : (get().workflowId === workflow.itemId ? get().executedItems : (workflow.items ?? []));
      
    const executedNodes = (workflow.nodeExecutions && workflow.nodeExecutions.length > 0) 
      ? workflow.nodeExecutions 
      : (get().workflowId === workflow.itemId ? get().executedNodes : (workflow.nodeExecutions ?? []));

    set({
      nodesMap,
      edgesMap,
      workflowId: workflow.itemId || null,
      workflowName: workflow.name || "",
      isPublished: workflow.isPublished || false,
      hasUnsavedChanges: false,
      executedItems,
      executedNodes,
    });
  },


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
      hasUnsavedChanges: false,
      executedItems: [],
      executedNodes: [],
      stepExecutionReachableNodeIds: null,
      stepExecutionTraversedEdgeIds: null,
      isListening: false,
      listeningNodeId: null,
    });
  },

  setStepExecutionData: (execution) => {
    const { nodesMap, edgesMap, clearStepExecutionData } = get();
    const win = window as unknown as {
      executionAnimationInterval?: ReturnType<typeof setInterval> | null;
    };

    // Clear any existing animation
    if (win.executionAnimationInterval) {
      clearInterval(win.executionAnimationInterval);
    }

    // Create an array of workflow nodes to pass into buildExecutedSubgraph
    const nodesArray = Object.values(nodesMap) as unknown as Parameters<
      typeof buildExecutedSubgraph
    >[0];
    const edgesArray = Object.values(edgesMap) as unknown as Parameters<
      typeof buildExecutedSubgraph
    >[1];

    const { reachableNodeIds, traversedEdgeIds } = buildExecutedSubgraph(
      nodesArray,
      edgesArray,
      execution.data.nodeExecutions,
      execution.data.items
    );

    const lastExecutedNode = execution.data.nodeExecutions[execution.data.nodeExecutions.length - 1];
    if (lastExecutedNode) {
      const outgoingEdges = edgesArray.filter((e) => e.source === lastExecutedNode.nodeId);
      for (const edge of outgoingEdges) {
        traversedEdgeIds.add(edge.id);
      }
    }

    // Clear first to allow the UI to reset
    clearStepExecutionData();

    // First set the actual data but leave the display sets empty to start the animation
    set({
      executedItems: execution.data.items,
      executedNodes: execution.data.nodeExecutions,
      stepExecutionReachableNodeIds: new Set(),
      stepExecutionTraversedEdgeIds: new Set(),
    });

    const nodesToAnimate = Array.from(reachableNodeIds);
    let currentIndex = 0;

    // Use window to store interval to survive state re-creations just in case
    win.executionAnimationInterval = setInterval(() => {
      if (currentIndex >= nodesToAnimate.length) {
        clearInterval(win.executionAnimationInterval ?? undefined);
        win.executionAnimationInterval = null;
        
        // Final safety sync
        set({
          stepExecutionReachableNodeIds: reachableNodeIds,
          stepExecutionTraversedEdgeIds: traversedEdgeIds,
        });
        return;
      }

      const nodeId = nodesToAnimate[currentIndex];

      set((state) => {
        const newNodesSet = new Set(state.stepExecutionReachableNodeIds || []);
        newNodesSet.add(nodeId);

        const newEdgesSet = new Set(state.stepExecutionTraversedEdgeIds || []);
        for (const edgeId of traversedEdgeIds) {
          const edge = edgesArray.find(e => e.id === edgeId);
          // Highlight edges that start from a highlighted node
          if (edge && newNodesSet.has(edge.source)) {
             newEdgesSet.add(edgeId);
          }
        }

        return {
          stepExecutionReachableNodeIds: newNodesSet,
          stepExecutionTraversedEdgeIds: newEdgesSet,
        };
      });

      currentIndex++;
    }, 150); // 250ms cascading delay between nodes
  },

  setLastSuccessfulExecutionData: (data) => set({ lastSuccessfulExecutionData: data }),

  clearStepExecutionData: () => {
    set({
      stepExecutionReachableNodeIds: null,
      stepExecutionTraversedEdgeIds: null,
      // We don't necessarily clear executedItems and executedNodes 
      // if we only want to hide the visual layer, but clearing them prevents 
      // the node inspector from showing leftover execution data.
      executedItems: [],
      executedNodes: [],
    });
  },

  tidyUpWorkflow: () => {
    const { nodesMap, edgesMap } = get();
    const newNodesMap = getLayoutedElements(nodesMap, edgesMap);
    set({
      nodesMap: newNodesMap,
      hasUnsavedChanges: true,
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
