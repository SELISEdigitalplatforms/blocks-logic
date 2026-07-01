import { useMemo, useState, useEffect } from "react";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useWorkflowStore } from "@blocks-workflow/store";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { getAllPredecessors } from "../../../utils/predecessor.util";
import { ChevronDown, ChevronUp } from "lucide-react";

function DraggableProperty({ fieldKey, prefixPath = "", label, depth = 0, nodeName, hasSinglePredecessor, isRoot = false }: { fieldKey?: string; prefixPath?: string; label?: string; depth?: number; nodeName: string; hasSinglePredecessor: boolean; isRoot?: boolean }) {
  const expressionPath = prefixPath ? (fieldKey ? `${prefixPath}.${fieldKey}` : prefixPath) : (fieldKey || "");
  
  const expression = hasSinglePredecessor
    ? `{{$json.output${expressionPath ? '.' + expressionPath : ''}}}`
    : `{{$node["${nodeName}"].json.output${expressionPath ? '.' + expressionPath : ''}}}`;

  return (
    <div
      draggable={true}
      onDragStart={(e) => {
        e.dataTransfer.setData("text/plain", expression);
        e.dataTransfer.dropEffect = "copy";
      }}
      className={`hover:bg-surface-hover group flex cursor-pointer items-center gap-2 rounded px-2 py-1 text-xs touch-none select-none`}
      style={{ marginLeft: `${depth * 1}rem` }}
      title={`Drag to use: ${expression}`}
    >
      <span className="font-mono text-high-emphasis cursor-grab active:cursor-grabbing">{label || fieldKey || "output"}:</span>
    </div>
  );
}

function RecursiveSchemaViewer({ data, depth = 0, prefixPath = "", nodeName, hasSinglePredecessor }: { data: unknown; depth?: number; prefixPath?: string; nodeName: string; hasSinglePredecessor: boolean }) {
  if (typeof data !== "object" || data === null) {
    if (!prefixPath) {
      return (
        <div className="flex items-center gap-2">
           <DraggableProperty
            fieldKey=""
            nodeName={nodeName}
            hasSinglePredecessor={hasSinglePredecessor}
            label="(value)"
            isRoot={true}
          />
          <span className="text-low-emphasis">{formatCellValue(data)}</span>
        </div>
      );
    }
    return <span className="text-low-emphasis px-2 py-1">{formatCellValue(data)}</span>;
  }

  const isArray = Array.isArray(data);
  const entries = Object.entries(data);

  return (
    <div className="flex flex-col w-full">
      {entries.map(([key, val]) => {
        const currentPath = prefixPath ? (isArray ? `${prefixPath}[${key}]` : `${prefixPath}.${key}`) : (isArray ? `[${key}]` : key);
        const childIsObj = typeof val === "object" && val !== null;

        return (
          <div key={key} className="flex flex-col">
            <div className="flex items-center gap-2">
              {!isArray ? (
                <DraggableProperty
                  fieldKey={key}
                  prefixPath={prefixPath}
                  depth={depth}
                  nodeName={nodeName}
                  hasSinglePredecessor={hasSinglePredecessor}
                  label={key}
                />
              ) : (
                <span className="text-low-emphasis px-2 py-1" style={{ marginLeft: `${depth * 1}rem` }}>[{key}]:</span>
              )}
              {!childIsObj && <span className="text-low-emphasis">{formatCellValue(val)}</span>}
            </div>
            {childIsObj && (
              <RecursiveSchemaViewer
                data={val}
                depth={depth + 1}
                prefixPath={currentPath}
                nodeName={nodeName}
                hasSinglePredecessor={hasSinglePredecessor}
              />
            )}
          </div>
        );
      })}
    </div>
  );
}

function RecursiveJsonViewer({ data, depth = 0, prefixPath = "", nodeName, hasSinglePredecessor }: { data: unknown; depth?: number; prefixPath?: string; nodeName: string; hasSinglePredecessor: boolean }) {
  if (typeof data !== "object" || data === null) {
    if (!prefixPath) {
      return (
        <div className="flex items-center gap-2">
           <DraggableProperty
            fieldKey=""
            nodeName={nodeName}
            hasSinglePredecessor={hasSinglePredecessor}
            label="(value)"
            isRoot={true}
          />
          <span className="text-green-600 dark:text-green-400">{formatCellValue(data)}</span>
        </div>
      );
    }
    return <span className="text-green-600 dark:text-green-400">{formatCellValue(data)}</span>;
  }

  const entries = Object.entries(data);
  const isArray = Array.isArray(data);

  return (
    <div className="flex flex-col w-full font-mono text-xs">
      <span style={{ marginLeft: `${depth * 1}rem` }}>{isArray ? "[" : "{"}</span>
      {entries.map(([key, val], index) => {
        const currentPath = prefixPath ? (isArray ? `${prefixPath}[${key}]` : `${prefixPath}.${key}`) : (isArray ? `[${key}]` : key);
        const childIsObj = typeof val === "object" && val !== null;

        return (
          <div key={key} className="flex flex-col">
            <div className="flex items-center" style={{ marginLeft: `${(depth + 1) * 1}rem` }}>
              {!isArray && (
                <DraggableProperty
                  fieldKey={key}
                  prefixPath={prefixPath}
                  depth={0}
                  nodeName={nodeName}
                  hasSinglePredecessor={hasSinglePredecessor}
                  label={`"${key}"`}
                />
              )}
              {!isArray && <span className="mx-1">:</span>}
              {!childIsObj ? (
                <span className="text-green-600 dark:text-green-400">
                  {formatCellValue(val)}{index < entries.length - 1 ? "," : ""}
                </span>
              ) : null}
            </div>
            {childIsObj && (
              <div className="flex flex-col">
                <RecursiveJsonViewer
                  data={val}
                  depth={depth + 1}
                  prefixPath={currentPath}
                  nodeName={nodeName}
                  hasSinglePredecessor={hasSinglePredecessor}
                />
                <span style={{ marginLeft: `${(depth + 1) * 1}rem` }}>{index < entries.length - 1 ? "," : ""}</span>
              </div>
            )}
          </div>
        );
      })}
      <span style={{ marginLeft: `${depth * 1}rem` }}>{isArray ? "]" : "}"}</span>
    </div>
  );
}

export const InputPanel = ({
  isCollapsed,
  onToggleCollapse,
}: {
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
} = {}) => {
  const [tab, setTab] = useState("schema");
  const selectedNode = useWorkflowStore((s) => s.selectedNode);
  const executedNodes = useWorkflowStore((s) => s.executedNodes);
  const executedItems = useWorkflowStore((s) => s.executedItems);
  const edgesMap = useWorkflowStore((s) => s.edgesMap);
  const nodesMap = useWorkflowStore((s) => s.nodesMap);

  const predecessors = useMemo(() => {
    if (!selectedNode) return [];
    return getAllPredecessors(selectedNode.id, nodesMap, edgesMap, executedItems);
  }, [selectedNode, edgesMap, nodesMap, executedItems]);

  const [selectedPredecessorId, setSelectedPredecessorId] = useState<string | null>(null);

  useEffect(() => {
    if (predecessors.length > 0) {
      if (!selectedPredecessorId || !predecessors.find((p) => p.id === selectedPredecessorId)) {
        setSelectedPredecessorId(predecessors[0].id);
      }
    } else {
      setSelectedPredecessorId(null);
    }
  }, [predecessors, selectedPredecessorId]);

  const activePredecessor = useMemo(() => {
    return predecessors.find((p) => p.id === selectedPredecessorId);
  }, [predecessors, selectedPredecessorId]);

  const runtimeInputRows = useMemo(() => {
    if (predecessors.length > 0 && selectedPredecessorId) {
      return executedNodes.find((en) => en.nodeId === selectedPredecessorId)?.output || [];
    }
    if (selectedNode) {
      return executedNodes.find((en) => en.nodeId === selectedNode.id)?.input || [];
    }
    return [];
  }, [executedNodes, selectedNode, predecessors, selectedPredecessorId]);

  if (!selectedNode) return null;

  return (
    <div className={`flex w-full flex-col overflow-hidden ${isCollapsed ? 'h-fit shrink-0' : 'h-full flex-1'}`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="font-medium text-high-emphasis">Input</h3>
          {predecessors.length > 0 && (
            <Select value={selectedPredecessorId || ""} onValueChange={setSelectedPredecessorId}>
              <SelectTrigger className="h-7 w-[160px] text-xs">
                <SelectValue placeholder="Select node" />
              </SelectTrigger>
              <SelectContent>
                {predecessors.map((p) => (
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
          {runtimeInputRows.length === 0 ? (
            <p className="text-xs text-low-emphasis">No runtime input schema available.</p>
          ) : (
            <div className="flex flex-col gap-3">
              {runtimeInputRows.map((row, index) => (
                <div key={index} className="rounded border border-border/60 p-2">
                  <p className="mb-1 text-xs font-semibold text-medium-emphasis">item {index + 1}:</p>
                  <RecursiveSchemaViewer 
                    data={row} 
                    nodeName={activePredecessor?.name || selectedNode.name} 
                    hasSinglePredecessor={predecessors.length === 1}
                  />
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {!isCollapsed && tab === "table" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          <RuntimeTable 
            rows={runtimeInputRows} 
            nodeName={activePredecessor?.name || selectedNode.name} 
            hasSinglePredecessor={predecessors.length === 1}
          />
        </div>
      )}

      {!isCollapsed && tab === "json" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          <RuntimeJson 
            rows={runtimeInputRows} 
            nodeName={activePredecessor?.name || selectedNode.name} 
            hasSinglePredecessor={predecessors.length === 1}
          />
        </div>
      )}
    </div>
  );
};

function RuntimeTable({ rows, nodeName, hasSinglePredecessor }: { rows: unknown[]; nodeName: string; hasSinglePredecessor: boolean }) {
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime input data available.</p>;
  }

  const primitiveColumn = "(value)";
  const hasPrimitiveRows = rows.some((row) => typeof row !== "object" || row === null);
  const objectColumns = Array.from(
    rows.reduce<Set<string>>((acc, row) => {
      if (typeof row !== "object" || row === null) return acc;
      Object.keys(row).forEach((key) => acc.add(key));
      return acc;
    }, new Set<string>()),
  );

  const columns: string[] = hasPrimitiveRows ? [primitiveColumn, ...objectColumns] : objectColumns;

  return (
    <ScrollArea className="h-full w-full whitespace-nowrap rounded">
      <table className="min-w-max border-separate border-spacing-0 text-xs">
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column}
                className="border-b border-border px-2 py-1 text-left font-semibold text-medium-emphasis align-top"
              >
                {column === primitiveColumn ? (
                  <DraggableProperty
                    fieldKey=""
                    nodeName={nodeName}
                    hasSinglePredecessor={hasSinglePredecessor}
                    label={column}
                    isRoot={true}
                  />
                ) : (
                  <DraggableProperty
                    fieldKey={column}
                    nodeName={nodeName}
                    hasSinglePredecessor={hasSinglePredecessor}
                    label={column}
                  />
                )}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => {
            const isRowObj = typeof row === "object" && row !== null;
            return (
              <tr key={index}>
                {columns.map((column) => (
                  <td key={column} className="border-b border-border/50 px-2 py-1 text-high-emphasis align-top">
                    {column === primitiveColumn
                      ? !isRowObj
                        ? formatCellValue(row)
                        : ""
                      : isRowObj
                        ? (typeof (row as any)[column] === "object" && (row as any)[column] !== null) ? (
                            <RecursiveSchemaViewer 
                              data={(row as any)[column]} 
                              depth={0}
                              prefixPath={column}
                              nodeName={nodeName}
                              hasSinglePredecessor={hasSinglePredecessor}
                            />
                          ) : formatCellValue((row as any)[column])
                        : ""}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
      <ScrollBar orientation="horizontal" />
    </ScrollArea>
  );
}

function RuntimeJson({ rows, nodeName, hasSinglePredecessor }: { rows: unknown[]; nodeName: string; hasSinglePredecessor: boolean }) {
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime input data available.</p>;
  }

  return (
    <div className="flex flex-col gap-3">
      {rows.map((row, index) => (
        <div key={index} className="rounded border border-border/60 p-2">
          <p className="mb-1 text-xs font-semibold text-medium-emphasis">item {index + 1}:</p>
          <RecursiveJsonViewer 
            data={row} 
            nodeName={nodeName} 
            hasSinglePredecessor={hasSinglePredecessor}
          />
        </div>
      ))}
    </div>
  );
}

function formatCellValue(value: unknown): string {
  if (value === null || value === undefined) return String(value);
  if (typeof value === "string") return `"${value}"`;
  if (typeof value === "number" || typeof value === "boolean") return String(value);

  try {
    return JSON.stringify(value);
  } catch {
    return "[unserializable]";
  }
}
