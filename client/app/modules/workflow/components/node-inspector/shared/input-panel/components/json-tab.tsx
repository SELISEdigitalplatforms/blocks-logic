import { DraggableProperty } from "./draggable-property";
import { formatCellValue } from "../utils/format.util";

function RecursiveJsonViewer({ data, depth = 0, prefixPath = "", nodeName, hasSinglePredecessor, isDraggable = true }: { data: unknown; depth?: number; prefixPath?: string; nodeName: string; hasSinglePredecessor: boolean; isDraggable?: boolean }) {
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
            isDraggable={isDraggable}
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
                  isDraggable={isDraggable}
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
                  isDraggable={isDraggable}
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

export function JsonTab({ rows, nodeName, hasSinglePredecessor, isDraggable = true }: { rows: unknown[]; nodeName: string; hasSinglePredecessor: boolean; isDraggable?: boolean }) {
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
            isDraggable={isDraggable}
          />
        </div>
      ))}
    </div>
  );
}
