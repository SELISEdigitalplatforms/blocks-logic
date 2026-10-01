import { Button } from "@/components/ui-kits/button/button";
import { copyToClipboard } from "@blocks-workflow/utils/copy-to-clipboard";
import { Copy } from "lucide-react";
import { DraggableArrayIndex, DraggableProperty } from "./draggable-property";
import { formatCellValue } from "../utils/format.util";
import { isPlainObject, type FieldReferenceTarget, type PathSegment } from "../utils/field-reference.util";

type RecursiveJsonViewerProps = {
  data: unknown;
  depth?: number;
  segments?: PathSegment[];
  nodeName: string;
  isDirectParent: boolean;
  itemIndex: number;
  itemIsObject: boolean;
  target?: FieldReferenceTarget;
  isDraggable?: boolean;
};

function RecursiveJsonViewer({ data, depth = 0, segments = [], nodeName, isDirectParent, itemIndex, itemIsObject, target, isDraggable = true }: RecursiveJsonViewerProps) {
  const referenceProps = { nodeName, isDirectParent, itemIndex, itemIsObject, target };

  if (typeof data !== "object" || data === null) {
    if (segments.length === 0) {
      return (
        <div className="flex items-center gap-2">
           <DraggableProperty
            segments={[]}
            {...referenceProps}
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
        const currentSegments = [...segments, isArray ? Number(key) : key];
        const childIsObj = typeof val === "object" && val !== null;

        return (
          <div key={key} className="flex flex-col">
            <div className="flex items-center" style={{ marginLeft: `${(depth + 1) * 1}rem` }}>
              {isArray ? (
                <DraggableArrayIndex
                  segments={currentSegments}
                  label={`[${key}]`}
                  depth={0}
                  {...referenceProps}
                  isDraggable={isDraggable}
                  showColon={false}
                />
              ) : (
                <DraggableProperty
                  segments={currentSegments}
                  depth={0}
                  {...referenceProps}
                  label={`"${key}"`}
                  isDraggable={isDraggable}
                />
              )}
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
                  segments={currentSegments}
                  {...referenceProps}
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

export function JsonTab({ rows, nodeName, isDirectParent, target, isDraggable = true }: { rows: unknown[]; nodeName: string; isDirectParent: boolean; target?: FieldReferenceTarget; isDraggable?: boolean }) {
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime input data available.</p>;
  }

  const json = JSON.stringify(rows, null, 2);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex justify-end">
        <Button
          type="button"
          variant="ghost"
          size="xxs"
          className="h-7 px-2 text-low-emphasis"
          aria-label="Copy input JSON"
          title="Copy input JSON"
          onClick={() => void copyToClipboard(json)}
        >
          <Copy className="h-4 w-4" />
        </Button>
      </div>
      {rows.map((row, index) => (
        <div key={index} className="rounded border border-border/60 p-2">
          <p className="mb-1 text-xs font-semibold text-medium-emphasis">item {index + 1}:</p>
          <RecursiveJsonViewer 
            data={row} 
            nodeName={nodeName} 
            isDirectParent={isDirectParent}
            itemIndex={index}
            itemIsObject={isPlainObject(row)}
            target={target}
            isDraggable={isDraggable}
          />
        </div>
      ))}
    </div>
  );
}
