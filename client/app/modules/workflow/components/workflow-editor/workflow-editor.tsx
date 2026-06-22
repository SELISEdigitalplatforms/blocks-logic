"use client";
import { useEffect } from "react";
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

export const WorkflowEditor = () => {
  const {
    nodes,
    edges,
    selectedNode,
    onNodeClick,
    onNodesChange,
    onEdgesChange,
    onConnect,
    isValidConnection,
    openNodeLibraryPanel,
    copyNode,
    pasteNode,
  } = useWorkflow();

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
        if (selectedNode) {
          copyNode(selectedNode.id);
        }
      }

      // Ctrl/Cmd + V
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "v") {
        pasteNode();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [selectedNode, copyNode, pasteNode]);

  return (
    <>
      <div className="relative h-full w-full">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onConnect={onConnect}
          onNodeClick={onNodeClick}
          isValidConnection={isValidConnection}
          nodeTypes={WorkflowEditorNodeTypes}
          defaultEdgeOptions={WorkflowEditorDefaultEdgeOptions}
          edgeTypes={WorkflowEditorEdgeTypes}
          className="bg-background"
          fitView={EditorFitConfig.fitView}
          fitViewOptions={EditorFitConfig.fitViewOptions}
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
