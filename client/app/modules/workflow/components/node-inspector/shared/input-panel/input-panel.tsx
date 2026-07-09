import { useMemo, useState, useEffect } from "react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useWorkflow } from "@blocks-workflow/hooks";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { getAllPredecessors } from "../../../../utils/predecessor.util";
import { ChevronDown, ChevronUp } from "lucide-react";

import { SchemaTab } from "./components/schema-tab";
import { TableTab } from "./components/table-tab";
import { JsonTab } from "./components/json-tab";

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
  const isLastExecutionEditor = editorMode === "editor" && !hasStepExecutionData && hasLastExecutionData;
  
  const sourceExecutedNodes = (isStepExecutionEditor || isExecutionMode) 
    ? executedNodes 
    : (isLastExecutionEditor && lastSuccessfulExecutionData?.data ? lastSuccessfulExecutionData.data.nodeExecutions || [] : []);

  const sourceExecutedItems = (isStepExecutionEditor || isExecutionMode)
    ? executedItems
    : (isLastExecutionEditor && lastSuccessfulExecutionData?.data ? (lastSuccessfulExecutionData.data.items || []) : []);


  const predecessors = useMemo(() => {
    if (!selectedNode) return [];
    return getAllPredecessors(selectedNode.id, nodesMap, edgesMap, sourceExecutedItems as any);
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

  useEffect(() => {
    if (displayPredecessors.length > 0) {
      if (!selectedPredecessorId || !displayPredecessors.find((p) => p.id === selectedPredecessorId)) {
        setSelectedPredecessorId(displayPredecessors[0].id);
      }
    } else {
      setSelectedPredecessorId(null);
    }
  }, [displayPredecessors, selectedPredecessorId]);

  const activePredecessor = useMemo(() => {
    return displayPredecessors.find((p) => p.id === selectedPredecessorId);
  }, [displayPredecessors, selectedPredecessorId]);

  const runtimeInputRows = useMemo(() => {
    if (displayPredecessors.length > 0 && selectedPredecessorId) {
      return sourceExecutedNodes?.find((en) => en.nodeId === selectedPredecessorId)?.output || [];
    }
    if (selectedNode) {
      return sourceExecutedNodes?.find((en) => en.nodeId === selectedNode.id)?.input || [];
    }
    return [];
  }, [sourceExecutedNodes, selectedNode, displayPredecessors, selectedPredecessorId]);

  if (!selectedNode) return null;

  return (
    <div className={`flex w-full flex-col overflow-hidden ${isCollapsed ? 'h-fit shrink-0' : 'h-full flex-1'}`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="font-medium text-high-emphasis">Input</h3>
          {displayPredecessors.length > 1 && (
            <Select value={selectedPredecessorId || ""} onValueChange={setSelectedPredecessorId}>
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
            nodeName={activePredecessor?.name || selectedNode.name}
            hasSinglePredecessor={immediateParentIds.length === 1 && immediateParentIds[0] === activePredecessor?.id}
            isExecutionMode={isExecutionMode}
          />
        </div>
      )}

      {!isCollapsed && tab === "table" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {runtimeInputRows.length === 0 || isLastExecutionEditor ? (
            <p className="text-xs text-low-emphasis">{"No input data available."}{isLastExecutionEditor && " Execute Node to view."}</p>
          ) : (
            <TableTab 
              rows={runtimeInputRows} 
              nodeName={activePredecessor?.name || selectedNode.name} 
              hasSinglePredecessor={immediateParentIds.length === 1 && immediateParentIds[0] === activePredecessor?.id}
              isDraggable={!isExecutionMode}
            />
          )}
        </div>
      )}

      {!isCollapsed && tab === "json" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {runtimeInputRows.length === 0 || isLastExecutionEditor ? (
            <p className="text-xs text-low-emphasis">{"No input data available."}{isLastExecutionEditor && " Execute Node to view."}</p>
          ) : (
            <JsonTab 
              rows={runtimeInputRows} 
              nodeName={activePredecessor?.name || selectedNode.name} 
              hasSinglePredecessor={immediateParentIds.length === 1 && immediateParentIds[0] === activePredecessor?.id}
              isDraggable={!isExecutionMode}
            />
          )}
        </div>
      )}
    </div>
  );
};
