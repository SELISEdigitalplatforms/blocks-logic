import { DraggableArrayIndex, DraggableProperty } from "./draggable-property";
import { formatCellValue } from "../utils/format.util";

export function RecursiveSchemaViewer({ data, depth = 0, prefixPath = "", nodeName, hasSinglePredecessor, showValues = true, isDraggable = true, showColon = true }: { data: unknown; depth?: number; prefixPath?: string; nodeName: string; hasSinglePredecessor: boolean; showValues?: boolean; isDraggable?: boolean; showColon?: boolean }) {
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
            showColon={showColon}
          />
          {showValues && <span className="text-low-emphasis text-xs">{formatCellValue(data)}</span>}
        </div>
      );
    }
    return showValues ? <span className="text-low-emphasis text-xs px-2 py-1">{formatCellValue(data)}</span> : null;
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
                  isDraggable={isDraggable}
                  showColon={showColon}
                />
              ) : (
                <DraggableArrayIndex
                  expressionPath={currentPath}
                  label={`[${key}]`}
                  depth={depth}
                  nodeName={nodeName}
                  hasSinglePredecessor={hasSinglePredecessor}
                  isDraggable={isDraggable}
                  showColon={showColon}
                />
              )}
              {(!childIsObj && showValues) && <span className="text-low-emphasis text-xs">{formatCellValue(val)}</span>}
            </div>
            {childIsObj && (
              <RecursiveSchemaViewer
                data={val}
                depth={depth + 1}
                prefixPath={currentPath}
                nodeName={nodeName}
                hasSinglePredecessor={hasSinglePredecessor}
                showValues={showValues}
                isDraggable={isDraggable}
                showColon={showColon}
              />
            )}
          </div>
        );
      })}
    </div>
  );
}
