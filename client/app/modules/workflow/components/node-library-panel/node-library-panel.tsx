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
import { EditorNode, WorkflowNodeDefinition } from "@blocks-workflow/models/node.model";
import { cloneHandle, findNodeDefinition } from "../../utils/node-upgraders";
import { NodeSchemasDefinition } from "../node-schemas";
import { v4 as uuidv4 } from "uuid";
import { findFreePosition } from "../../utils/find-free-position";

// Panel order of the categories; nodes inside each are sorted by title.
const NODE_CATEGORIES = [
  { key: "trigger", label: "Triggers" },
  { key: "logic", label: "Logic" },
  { key: "transform", label: "Transform" },
  { key: "action", label: "Actions" },
];

type NodeLibraryPanelProps = {
  title?: string;
  description?: string;
};

export const NodeLibraryPanel = ({
  title = "Add a node",
  description = "Search or browse by category.",
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
    deselectAllEdges,
    deselectHandle,
    getNodeNextSource,
  } = useWorkflow();
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedCategory, setSelectedCategory] = useState("all");
  const onOpenChange = () => {
    closeNodeLibraryPanel();
    setSearchQuery("");
    setSelectedCategory("all");
  }

  const filteredOptions = NodeDefinitions.filter(
    (option) =>
      option.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
      option.description.toLowerCase().includes(searchQuery.toLowerCase()),
  );

  const groups = NODE_CATEGORIES.filter(
    (category) => selectedCategory === "all" || category.key === selectedCategory,
  )
    .map((category) => ({
      ...category,
      options: filteredOptions
        .filter((option) => option.category === category.key)
        .sort((a, b) => a.title.localeCompare(b.title)),
    }))
    .filter((group) => group.options.length > 0);

  // A workflow can have one Error Trigger.
  const hasErrorTrigger = nodes.some((n) => n.type === "errorTrigger");
  const isUnavailable = (node: WorkflowNodeDefinition) =>
    node.isComingSoon || (node.type === "errorTrigger" && hasErrorTrigger);

  const handleSelectOption = useCallback(
    (node: WorkflowNodeDefinition) => {
      if (isUnavailable(node)) return;
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

      // Only the node's own fields: the definition's icon, title and handle template stay in the definition.
      const nodeData: EditorNode = {
        id: uuidv4().replace(/-/g, ""),
        name:
          length > 0
            ? `${node.defaultName || node.type}${length + 1}`
            : node.defaultName || node.type,
        type: node.type,
        category: node.category,
        version: node.version,
        handle: cloneHandle(node.handle),
        parameters: schemaDefinition?.defaults.parameters || {},
        settings: schemaDefinition?.defaults.settings || {},
        position,
        data: {
          hasHandleArrow: true,
        },
      };
      const transformedNode = schemaDefinition?.transform?.(nodeData) ?? nodeData;
      addNode(transformedNode);
      const selectedNodesToConnect = nodes.filter((n) => n.selected);
      if (selectedNodesToConnect.length === 0 && selectedNode) {
        selectedNodesToConnect.push(selectedNode);
      }

      const newTargets = transformedNode.handle?.targets ?? [];
      if (selectedNodesToConnect.length > 0 && newTargets.length > 0) {
        selectedNodesToConnect.forEach((selNode) => {
          const selectedSources = selNode.handle?.sources ?? findNodeDefinition(selNode)?.handle.sources;
          const sourceHandle = selectedSources
            ? getNodeNextSource(selNode.id, selectedSources.map((entry) => entry.key))
            : "source";
          
          const edgeSourceHandle =
            selectedNodesToConnect.length === 1 && selectedHandle
              ? selectedHandle
              : sourceHandle;

          createEdge(
            { source: selNode.id, sourceHandle: edgeSourceHandle },
            { target: transformedNode.id, targetHandle: newTargets[0].key },
          );
        });
      }
      deselectAllEdges();
      selectNode(transformedNode);
      deselectHandle();
      closeNodeLibraryPanel();
    },
    // selectedHandle is deliberately left out (unchanged from before): adding it changes which
    // handle value this callback reads, i.e. behavior. Tracked separately as a possible stale read.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [addNode, closeNodeLibraryPanel, nodes, selectedNode, createEdge, selectNode, deselectAllEdges, getNodeNextSource, deselectHandle],
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
          <div className="flex flex-col gap-3 border-b px-6 py-4">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                placeholder="Search nodes"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="pl-9"
              />
            </div>
            <div role="tablist" aria-label="Node categories" className="flex flex-wrap gap-1.5">
              {[{ key: "all", label: "All" }, ...NODE_CATEGORIES].map((category) => (
                <button
                  key={category.key}
                  type="button"
                  role="tab"
                  aria-selected={selectedCategory === category.key}
                  onClick={() => setSelectedCategory(category.key)}
                  className={cn(
                    "h-8 rounded-full border px-3 text-sm font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                    selectedCategory === category.key
                      ? "border-primary bg-primary/15 text-high-emphasis"
                      : "text-medium-emphasis hover:bg-accent",
                  )}
                >
                  {category.label}
                </button>
              ))}
            </div>
          </div>
          <div className="flex-1 overflow-y-auto px-4 pb-6">
            {groups.map((group) => (
              <section key={group.key} aria-label={group.label}>
                <h3 className="px-3 pb-1.5 pt-4 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  {group.label}
                </h3>
                {group.options.map((option) => (
                  <button
                    key={option.id}
                    type="button"
                    onClick={() => handleSelectOption(option)}
                    aria-disabled={option.type === "errorTrigger" && hasErrorTrigger ? true : undefined}
                    className={cn(
                      "flex w-full cursor-pointer items-start gap-3 rounded-md px-3 py-2.5 text-left transition-colors hover:bg-accent focus:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                      option.type === "errorTrigger" && hasErrorTrigger && "cursor-not-allowed opacity-50 hover:bg-transparent",
                    )}
                  >
                    <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-accent">
                      {option.icon}
                    </span>
                    <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                      <span className="flex items-center gap-2">
                        <span className="text-sm font-semibold text-high-emphasis">{option.title}</span>
                        {option.isComingSoon && (
                          <Badge variant="info" className="ml-auto h-fit w-fit">
                            Coming soon
                          </Badge>
                        )}
                      </span>
                      <span className="text-xs leading-relaxed text-muted-foreground">
                        {option.description}
                      </span>
                    </span>
                  </button>
                ))}
              </section>
            ))}
            {groups.length === 0 && (
              <div className="py-8 text-center text-sm text-muted-foreground">
                No nodes match &quot;{searchQuery}&quot;
              </div>
            )}
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
};
