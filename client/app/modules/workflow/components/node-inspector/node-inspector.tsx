"use client";
import { Sheet, SheetContent } from "@/components/ui-kits/sheet/sheet";
import { useWorkflow } from "../../hooks";
import { LayoutWithIO, LayoutWithListener } from "./layouts";
import { NodeSchemasDefinition } from "../node-schemas/node-schemas";
import { NodeSchema } from "../node-schemas/node-schema.type";
import type { ComponentType } from "react";

export type NodeInspectorLayoutProps = {
  schema: NodeSchema | null;
  guide?: ComponentType | null;
};

export const NodeInspectorLayouts: Record<string, React.ComponentType<NodeInspectorLayoutProps>> = {
  webhook: LayoutWithListener,
};

export const NodeInspector = () => {
  const { selectedNode, isConfigModalOpen, deselectNode } = useWorkflow();

  if (!selectedNode) return null;

  const NodeInspectorLayout = NodeInspectorLayouts[selectedNode.type] || LayoutWithIO;
  const schemaKey =
    `${selectedNode.category}${selectedNode.type}${selectedNode.version}` as keyof typeof NodeSchemasDefinition;
  const nodeSchemaDefinition = NodeSchemasDefinition[schemaKey] || null;

  return (
    <Sheet
      open={isConfigModalOpen}
      modal={false}
      onOpenChange={(value) => {
        if (!value) deselectNode();
      }}
    >
      <SheetContent
        side="right"
        className="top-[60px] h-[calc(100vh-60px)] w-full p-5 sm:max-w-6xl"
        hideClose
        aria-describedby={undefined}
        onInteractOutside={(e) => {
          // Prevent Radix auto-dismiss — copyToClipboard creates a textarea
          // on document.body (outside sheet content) causing both
          // onPointerDownOutside and onFocusOutside to fire.
          // The drawer is closed via the header close button or node deselection.
          e.preventDefault();
        }}
      >
        <NodeInspectorLayout
          schema={nodeSchemaDefinition?.schema || null}
          guide={nodeSchemaDefinition?.guide || null}
        />
      </SheetContent>
    </Sheet>
  );
};
