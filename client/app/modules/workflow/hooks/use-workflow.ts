import { useCallback, useMemo } from "react";
import { Connection, Edge, Node, useReactFlow } from "@xyflow/react";
import { useWorkflowStore } from "../store";
import { Workflow } from "../models/workflow.model";
import { WorkflowNode } from "@blocks-workflow/models/node.model";
import { getOrderedNodeData } from "@blocks-workflow/utils/runtime-node-data";

export const useWorkflow = () => {
  const reactFlowInstance = useReactFlow();
  const nodesMap = useWorkflowStore((state) => state.nodesMap);
  const edgesMap = useWorkflowStore((state) => state.edgesMap);
  const selectedNode = useWorkflowStore((state) => state.selectedNode);
  const selectedHandle = useWorkflowStore((state) => state.selectedHandle);
  const isConfigModalOpen = useWorkflowStore((state) => state.isConfigModalOpen);
  const isPanelOpen = useWorkflowStore((state) => state.isPanelOpen);
  const workflowId = useWorkflowStore((state) => state.workflowId);
  const workflowName = useWorkflowStore((state) => state.workflowName);
  const hasUnsavedChanges = useWorkflowStore((state) => state.hasUnsavedChanges);
  const editorMode = useWorkflowStore((state) => state.editorMode);
  const executionMode = useWorkflowStore((state) => state.executionMode);
  
  // Compute nodes and edges arrays from objects
  const nodes = useMemo(() => Object.values(nodesMap), [nodesMap]);
  const edges = useMemo(() => Object.values(edgesMap), [edgesMap]);

  // Zustand store actions
  const onNodesChange = useWorkflowStore((state) => state.onNodesChange);
  const onEdgesChange = useWorkflowStore((state) => state.onEdgesChange);
  const onConnect = useWorkflowStore((state) => state.onConnect);
  const addNode = useWorkflowStore((state) => state.addNode);
  const updateNode = useWorkflowStore((state) => state.updateNode);
  const deleteNode = useWorkflowStore((state) => state.deleteNode);
  const duplicateNode = useWorkflowStore((state) => state.duplicateNode);
  const copyNode = useWorkflowStore((state) => state.copyNode);
  const copySelectedNodes = useWorkflowStore((state) => state.copySelectedNodes);
  const pasteNodes = useWorkflowStore((state) => state.pasteNodes);
  const createEdge = useWorkflowStore((state) => state.createEdge);
  const deleteEdge = useWorkflowStore((state) => state.deleteEdge);
  const selectNode = useWorkflowStore((state) => state.selectNode);
  const deselectNode = useWorkflowStore((state) => state.deselectNode);
  const selectHandle = useWorkflowStore((state) => state.selectHandle);
  const deselectHandle = useWorkflowStore((state) => state.deselectHandle);
  const openConfigModal = useWorkflowStore((state) => state.openConfigModal);
  const closeConfigModal = useWorkflowStore((state) => state.closeConfigModal);
  const openNodeLibraryPanel = useWorkflowStore((state) => state.openNodeLibraryPanel);
  const closeNodeLibraryPanel = useWorkflowStore((state) => state.closeNodeLibraryPanel);
  const setWorkflow = useWorkflowStore((state) => state.setWorkflow);
  const setEditorMode = useWorkflowStore((state) => state.setEditorMode);
  const setExecutionMode = useWorkflowStore((state) => state.setExecutionMode);
  const resetWorkflow = useWorkflowStore((state) => state.resetWorkflow);
  const tidyUpWorkflow = useWorkflowStore((state) => state.tidyUpWorkflow);
  const getNodeById = useWorkflowStore((state) => state.getNodeById);
  const getEdgeById = useWorkflowStore((state) => state.getEdgeById);
  const executedItems = useWorkflowStore((state) => state.executedItems);

  const selectAndConfigureNode = useCallback(
    (node: WorkflowNode) => {
      selectNode(node);
      openConfigModal();
    },
    [selectNode, openConfigModal],
  );

  const getNodeEdges = useCallback(
    (nodeId: string) => {
      return {
        incoming: edges.filter((edge) => edge.target === nodeId),
        outgoing: edges.filter((edge) => edge.source === nodeId),
      };
    },
    [edges],
  );

  const getNodeNextSource = useCallback(
    (nodeId: string, handleSpecSource: string[]) => {
      const outgoingEdges = edges.filter((edge) => edge.source === nodeId);

      for (const source of handleSpecSource) {
        if (!outgoingEdges.some((edge) => edge.sourceHandle === source)) {
          return source;
        }
      }

      return handleSpecSource[0] ?? "source";
    },
    [edges],
  );

  // const exportWorkflow = useCallback(() => {
  //   return {
  //     id: workflowId,
  //     name: workflowName,
  //     isActive,
  //     nodes,
  //     edges,
  //     metadata: {
  //       version: "1.0",
  //       exportedAt: new Date().toISOString(),
  //     },
  //   };
  // }, [workflowId, workflowName, isActive, nodes, edges]);

  // const importWorkflow = useCallback(
  //   (data: Workflow) => {
  //     setWorkflow(data);
  //   },
  //   [setWorkflow],
  // );

  const validateWorkflow = useCallback(() => {}, []);

  const getWorkflowStats = useMemo(() => {
    return {
      totalNodes: nodes.length,
      totalEdges: edges.length,
      hasUnsavedChanges: hasUnsavedChanges,
    };
  }, [nodes, edges, hasUnsavedChanges]);

  const onNodeClick = useCallback(
    (event: React.MouseEvent, node: Node) => {
      if (event.ctrlKey || event.metaKey || event.shiftKey) {
        return;
      }
      const nodeData = getNodeById(node.id);
      if (!nodeData) return;
      selectAndConfigureNode(nodeData);
    },
    [getNodeById, selectAndConfigureNode],
  );

  const isValidConnection = useCallback((connection: Edge | Connection) => {
    if (connection.source === connection.target) return false;
    return true;
  }, []);

  const getNodeOutput = useCallback(
    (nodeId: string) => {
      const node = getNodeById(nodeId);
      if (!node) return null;
      return getOrderedNodeData(executedItems, nodeId, "Output");
    },
    [getNodeById, executedItems],
  );

  const getNodeInput = useCallback(
    (nodeId: string) => {
      const node = getNodeById(nodeId);
      if (!node) return null;
      return getOrderedNodeData(executedItems, nodeId, "Input");
    },
    [getNodeById, executedItems],
  );

  const isNodeNameUnique = useCallback(
    (name: string, excludeNodeId?: string) => {
      const lowerName = name.trim().toLowerCase();
      return !nodes.some(
        (node) => node.name?.toLowerCase() === lowerName && node.id !== excludeNodeId,
      );
    },
    [nodes],
  );

  // react flow instance methods

  const { fitView, zoomIn, zoomOut } = reactFlowInstance;

  return {
    // State
    nodes,
    edges,
    selectedNode,
    selectedHandle,
    isConfigModalOpen,
    isPanelOpen,
    workflowId,
    workflowName,
    hasUnsavedChanges,
    editorMode,
    executionMode,

    onNodesChange,
    onEdgesChange,
    onConnect,
    onNodeClick,
    isValidConnection,

    // Basic operations
    addNode,
    updateNode,
    deleteNode,
    duplicateNode,
    copyNode,
    copySelectedNodes,
    pasteNodes,
    createEdge,
    deleteEdge,

    // Selection operations
    selectNode,
    deselectNode,
    selectHandle,
    deselectHandle,
    selectAndConfigureNode,
    openConfigModal,
    closeConfigModal,

    // Panel operations
    openNodeLibraryPanel,
    closeNodeLibraryPanel,

    // Workflow operations
    setWorkflow,
    setEditorMode,
    setExecutionMode,
    resetWorkflow,
    tidyUpWorkflow,
    // exportWorkflow,
    // importWorkflow,

    // Utility methods
    getNodeById,
    getEdgeById,
    getNodeEdges,
    getNodeNextSource,

    validateWorkflow,
    getWorkflowStats,
    getNodeOutput,
    getNodeInput,
    isNodeNameUnique,

    // React Flow instance
    reactFlowInstance,

    fitView,
    zoomIn,
    zoomOut,
  };
};
