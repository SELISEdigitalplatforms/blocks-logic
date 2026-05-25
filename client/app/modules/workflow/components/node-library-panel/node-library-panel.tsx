"use client";

import { useCallback, useState } from "react";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui-kits/sheet/sheet";
import { Input } from "@/components/ui-kits/input/input";
import { Search } from "lucide-react";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui-kits/badge/badge";
import { useWorkflow } from "../../hooks";
import { NodeDefinitions } from "./node-definitions";
import { WorkflowNodeDefinition } from "@blocks-workflow/models/node.model";
import { NodeSchemasDefinition } from "../node-schemas";
import { v4 as uuidv4 } from "uuid";
import { findFreePosition } from "../../utils/find-free-position";

type NodeLibraryPanelProps = {
  title?: string;
  description?: string;
};

export const NodeLibraryPanel = ({
  title = "Start your workflow",
  description = "Select how this workflow should start.",
}: NodeLibraryPanelProps) => {
  const {
    isPanelOpen,
    closeNodeLibraryPanel,
    addNode,
    nodes,
    selectedNode,
    selectedHandle,
    createEdge,
    selectNode,
    deselectHandle,
    getNodeNextSource,
  } = useWorkflow();
  const [searchQuery, setSearchQuery] = useState("");
  const onOpenChange = () => {
    closeNodeLibraryPanel();
    setSearchQuery("");
  }

  const filteredOptions = NodeDefinitions.filter(
    (option) =>
      option.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
      option.description.toLowerCase().includes(searchQuery.toLowerCase()),
  );

  const handleSelectOption = useCallback(
    (node: WorkflowNodeDefinition) => {
      if (node.isComingSoon) return;
      const schemaDefinition = NodeSchemasDefinition[`${node.category}${node.type}${node.version}`];

      const length = nodes.filter((n) => n.type === node.type).length;

      // Calculate free position based on existing nodes to avoid overlaps
      const preferredPosition = selectedNode
        ? {
            x: selectedNode.position.x + 260,
            y: selectedNode.position.y,
          }
        : undefined;

      const position = findFreePosition(nodes, preferredPosition);

      const nodeData = {
        ...node,
        id: uuidv4().replace(/-/g, ""),
        name:
          length > 0
            ? `${node.defaultName || node.type}${length + 1}`
            : node.defaultName || node.type,
        type: node.type,
        category: node.category,
        parameters: schemaDefinition?.defaults.parameters || {},
        settings: schemaDefinition?.defaults.settings || {},
        position,
        data: {
          hasHandleArrow: true,
        },
      };
      const transformedNode = schemaDefinition?.transform?.(nodeData) ?? nodeData;
      addNode(transformedNode);
      if (selectedNode && node.handleSpec.target.length > 0) {
        const selectedDef = NodeDefinitions.find(
          (d) => d.type === selectedNode.type && d.category === selectedNode.category,
        );
        const sourceHandle = selectedDef
          ? getNodeNextSource(selectedNode.id, selectedDef.handleSpec.source)
          : "source";
        createEdge(
          { source: selectedNode.id, sourceHandle: selectedHandle || sourceHandle },
          { target: transformedNode.id, targetHandle: node.handleSpec.target[0] },
        );
      }
      selectNode(transformedNode);
      deselectHandle();
      closeNodeLibraryPanel();
    },
    [addNode, closeNodeLibraryPanel, nodes, selectedNode, createEdge, selectNode, getNodeNextSource, deselectHandle],
  );
  return (
    <Sheet open={isPanelOpen} onOpenChange={onOpenChange} modal={false}>
      <SheetContent
        side="right"
        className="top-[60px] h-[calc(100vh-60px)] w-full p-0 sm:max-w-md"
        onInteractOutside={(event) => {
          const target = event.target as HTMLElement;
          if (target.closest("[data-sheet-ignore]")) {
            event.preventDefault();
          }
        }}
      >
        <div className="flex h-full flex-col">
          <SheetHeader className="border-b px-6 py-4">
            <div className="flex items-start justify-between">
              <div>
                <SheetTitle>{title}</SheetTitle>
                <SheetDescription className="mt-1">{description}</SheetDescription>
              </div>
            </div>
          </SheetHeader>
          <div className="px-6 py-4">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                placeholder="Search"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="pl-9"
              />
            </div>
          </div>
          <div className="flex-1 overflow-y-auto px-6 pb-6">
            <div className="space-y-2">
              {filteredOptions.map((option) => (
                <div
                  key={option.id}
                  onClick={() => handleSelectOption(option)}
                  className={cn(
                    "flex w-full cursor-pointer flex-col gap-1.5 rounded-sm p-4 text-left text-medium-emphasis transition-colors hover:bg-accent focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2",
                  )}
                >
                  <div className="mt-0.5 flex items-center gap-1">
                    {option.icon}
                    <h4 className="text-sm font-medium text-high-emphasis">{option.title}</h4>
                    {option.isComingSoon && (
                      <Badge variant="info" className="ml-auto h-fit w-fit">
                        Coming soon
                      </Badge>
                    )}
                  </div>
                  <div className="flex-1">
                    <p className="text-xs leading-relaxed text-muted-foreground">
                      {option.description}
                    </p>
                  </div>
                </div>
              ))}
              {filteredOptions.length === 0 && (
                <div className="py-8 text-center text-sm text-muted-foreground">
                  No triggers found matching &quot;{searchQuery}&quot;
                </div>
              )}
            </div>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
};
