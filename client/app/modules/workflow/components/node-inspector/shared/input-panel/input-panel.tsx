import { useMemo, useState } from "react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useWorkflow } from "@blocks-workflow/hooks";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { getAllPredecessors } from "../../../../utils/predecessor.util";
import { ChevronDown, ChevronUp } from "lucide-react";
import { NodeSchemasDefinition } from "../../../node-schemas/node-schemas";
import { EXPRESSION_TARGET } from "./utils/field-reference.util";
import type { ExecutedItem, ExecutedNode } from "../../../../models/workflow.model";

import { SchemaTab } from "./components/schema-tab";
import { TableTab } from "./components/table-tab";
import { JsonTab } from "./components/json-tab";

const getLastExecutionNodes = (
  isLastExecutionEditor: boolean,
  lastSuccessfulExecutionData: { data?: { nodeExecutions?: ExecutedNode[] } } | null | undefined,
) => (isLastExecutionEditor ? (lastSuccessfulExecutionData?.data?.nodeExecutions ?? []) : []);

const getLastExecutionItems = (
  isLastExecutionEditor: boolean,
  lastSuccessfulExecutionData: { data?: { items?: ExecutedItem[] } } | null | undefined,
) => (isLastExecutionEditor ? (lastSuccessfulExecutionData?.data?.items ?? []) : []);

export const InputPanel = ({
  isCollapsed,
  onToggleCollapse,
}: {
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
} = {}) => {
  const [tab, setTab] = useState("schema");
  const {
    selectedNode,
    executedNodes,
    executedItems,
    edgesMap,
    nodesMap,
    editorMode,
    lastSuccessfulExecutionData,
  } = useWorkflow();

  const hasStepExecutionData = executedNodes && executedNodes.length > 0;
  const hasLastExecutionData = !!lastSuccessfulExecutionData;

  const isExecutionMode = editorMode === "execution";
  const isStepExecutionEditor = editorMode === "editor" && hasStepExecutionData;
  const isLastExecutionEditor =
    editorMode === "editor" && !hasStepExecutionData && hasLastExecutionData;

  const shouldUseCurrentExecution = isStepExecutionEditor || isExecutionMode;
  const sourceExecutedNodes: ExecutedNode[] = shouldUseCurrentExecution
    ? executedNodes
    : getLastExecutionNodes(isLastExecutionEditor, lastSuccessfulExecutionData);
  const sourceExecutedItems: ExecutedItem[] = shouldUseCurrentExecution
    ? executedItems
    : getLastExecutionItems(isLastExecutionEditor, lastSuccessfulExecutionData);

  const predecessors = useMemo(() => {
    if (!selectedNode) return [];
    return getAllPredecessors(selectedNode.id, nodesMap, edgesMap, sourceExecutedItems);
  }, [selectedNode, edgesMap, nodesMap, sourceExecutedItems]);

  const immediateParentIds = useMemo(() => {
    if (!selectedNode) return [];
    return Object.values(edgesMap)
      .filter((e) => e.target === selectedNode.id)
      .map((e) => e.source);
  }, [selectedNode, edgesMap]);

  const displayPredecessors = useMemo(() => {
    if (editorMode !== "editor") {
      return predecessors.filter((p) => immediateParentIds.includes(p.id));
    }
    return predecessors;
  }, [predecessors, immediateParentIds, editorMode]);

  const [selectedPredecessorId, setSelectedPredecessorId] = useState<string | null>(null);
  const resolvedPredecessorId =
    displayPredecessors.find((p) => p.id === selectedPredecessorId)?.id ??
    displayPredecessors[0]?.id ??
    null;

  const activePredecessor = useMemo(() => {
    return displayPredecessors.find((p) => p.id === resolvedPredecessorId);
  }, [displayPredecessors, resolvedPredecessorId]);

  const runtimeInputRows = useMemo(() => {
    if (displayPredecessors.length > 0 && resolvedPredecessorId) {
      return sourceExecutedNodes?.find((en) => en.nodeId === resolvedPredecessorId)?.output || [];
    }
    if (selectedNode) {
      return sourceExecutedNodes?.find((en) => en.nodeId === selectedNode.id)?.input || [];
    }
    return [];
  }, [sourceExecutedNodes, selectedNode, displayPredecessors, resolvedPredecessorId]);

  if (!selectedNode) return null;

  const isDirectParent =
    immediateParentIds.length === 1 && immediateParentIds[0] === activePredecessor?.id;
  const nodeName = activePredecessor?.name || selectedNode.name;
  const schemaKey = `${selectedNode.category}${selectedNode.type}${selectedNode.version}`;
  const target =
    NodeSchemasDefinition[schemaKey]?.fieldReference?.(selectedNode.parameters ?? {}) ??
    EXPRESSION_TARGET;

  return (
    <div
      className={`flex w-full flex-col overflow-hidden ${isCollapsed ? "h-fit shrink-0" : "h-full flex-1"}`}
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="font-medium text-high-emphasis">Input</h3>
          {displayPredecessors.length > 1 && (
            <Select value={resolvedPredecessorId || ""} onValueChange={setSelectedPredecessorId}>
              <SelectTrigger className="h-7 w-[160px] text-xs">
                <SelectValue placeholder="Select node" />
              </SelectTrigger>
              <SelectContent>
                {displayPredecessors.map((p) => (
                  <SelectItem key={p.id} value={p.id} className="text-xs">
                    {p.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Tabs value={tab} onValueChange={setTab}>
            <TabsList>
              <TabsTrigger value="schema">Schema</TabsTrigger>
              <TabsTrigger value="table">Table</TabsTrigger>
              <TabsTrigger value="json">JSON</TabsTrigger>
            </TabsList>
          </Tabs>
          {onToggleCollapse && (
            <button
              onClick={onToggleCollapse}
              className="text-low-emphasis hover:bg-surface-hover rounded p-1 transition-colors"
            >
              {isCollapsed ? <ChevronDown size={18} /> : <ChevronUp size={18} />}
            </button>
          )}
        </div>
      </div>

      {!isCollapsed && tab === "schema" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          <SchemaTab
            runtimeInputRows={runtimeInputRows}
            isLastExecutionEditor={isLastExecutionEditor}
            nodeName={nodeName}
            isDirectParent={isDirectParent}
            target={target}
            isExecutionMode={isExecutionMode}
          />
        </div>
      )}

      {!isCollapsed && tab === "table" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {runtimeInputRows.length === 0 || isLastExecutionEditor ? (
            <p className="text-xs text-low-emphasis">
              {"No input data available."}
              {isLastExecutionEditor && " Execute Node to view."}
            </p>
          ) : (
            <TableTab
              rows={runtimeInputRows}
              nodeName={nodeName}
              isDirectParent={isDirectParent}
              target={target}
              isDraggable={!isExecutionMode}
            />
          )}
        </div>
      )}

      {!isCollapsed && tab === "json" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {runtimeInputRows.length === 0 || isLastExecutionEditor ? (
            <p className="text-xs text-low-emphasis">
              {"No input data available."}
              {isLastExecutionEditor && " Execute Node to view."}
            </p>
          ) : (
            <JsonTab
              rows={runtimeInputRows}
              nodeName={nodeName}
              isDirectParent={isDirectParent}
              target={target}
              isDraggable={!isExecutionMode}
            />
          )}
        </div>
      )}
    </div>
  );
};
