"use client";
import { useEffect, useMemo } from "react";
import { ReactFlow, Background, BackgroundVariant } from "@xyflow/react";
import { Button } from "@/components/ui-kits/button/button";
import { Plus } from "lucide-react";
import { useWorkflow } from "../../hooks/use-workflow";
import { NodeLibraryPanel } from "../node-library-panel";
import { NodeInspector } from "../node-inspector";
import { WorkflowEditorEdgeTypes } from "../workflow-editor-edges";
import {
  WorkflowEditorDefaultEdgeOptions,
  WorkflowEditorNodeTypes,
} from "../workflow-editor-nodes";
import {
  EditorFitConfig,
  WorkflowEditorControls,
} from "../workflow-editor-controls";

interface WorkflowEditorProps {
  isReadonly?: boolean;
}

export const WorkflowEditor = ({ isReadonly = false }: WorkflowEditorProps) => {
  const {
    nodes,
    edges,
    isListening,
    listeningNodeId,
    selectedNode,
    onNodeClick,
    onNodesChange,
    onEdgesChange,
    onConnect,
    isValidConnection,
    openNodeLibraryPanel,
    copySelectedNodes,
    pasteNodes,
    setEditorMode,
  } = useWorkflow();

  useEffect(() => {
    setEditorMode("editor");
  }, [setEditorMode]);

  const modifiedNodes = useMemo(() => {
    if (!isListening || !listeningNodeId) return nodes;
    return nodes.map((node) => {
      if (node.id === listeningNodeId) {
        return {
          ...node,
          className: `${node.className || ""} !ring-2 !ring-green-500 rounded-md before:content-[''] before:absolute before:-top-1.5 before:-right-1.5 before:h-3 before:w-3 before:bg-green-500 before:rounded-full before:z-50 after:content-[''] after:absolute after:-top-1.5 after:-right-1.5 after:h-3 after:w-3 after:bg-green-500 after:rounded-full after:animate-ping after:z-40`.trim(),
        };
      }
      return node;
    });
  }, [nodes, isListening, listeningNodeId]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Don't trigger if user is typing in an input/textarea
      if (
        e.target instanceof HTMLInputElement ||
        e.target instanceof HTMLTextAreaElement ||
        (e.target as HTMLElement).isContentEditable
      ) {
        return;
      }

      // Ctrl/Cmd + C
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "c") {
        copySelectedNodes();
      }

      // Ctrl/Cmd + V
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "v") {
        pasteNodes();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [selectedNode, copySelectedNodes, pasteNodes]);


  return (
    <>
      <div className="relative h-full w-full">
        <ReactFlow
          nodes={modifiedNodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          onNodeClick={onNodeClick}
          isValidConnection={isValidConnection}
          nodeTypes={WorkflowEditorNodeTypes}
          defaultEdgeOptions={WorkflowEditorDefaultEdgeOptions}
          edgeTypes={WorkflowEditorEdgeTypes}
          multiSelectionKeyCode={["Meta", "Control", "Shift"]}
          deleteKeyCode={["Backspace", "Delete"]}
          className="bg-background"
          fitView={EditorFitConfig.fitView}
          fitViewOptions={EditorFitConfig.fitViewOptions}
          nodesDraggable={!isReadonly}
          nodesConnectable={!isReadonly}
        >
          <Background
            variant={BackgroundVariant.Dots}
            gap={15}
            size={1.2}
            className="bg-surface-app opacity-60"
          />
          <WorkflowEditorControls />
        </ReactFlow>
        {nodes.length === 0 && (
          <div className="absolute left-1/2 top-1/2 flex -translate-x-1/2 -translate-y-1/2 transform flex-col items-center gap-4">
            <Button
              variant="outline"
              size="lg"
              data-sheet-ignore
              onClick={openNodeLibraryPanel}
              className="z-50 gap-2 shadow-md"
            >
              <Plus className="h-5 w-5" />
              Add first step
            </Button>
          </div>
        )}
        {selectedNode && <NodeInspector key={selectedNode.id} />}
      </div>
      <NodeLibraryPanel />
    </>
  );
};
