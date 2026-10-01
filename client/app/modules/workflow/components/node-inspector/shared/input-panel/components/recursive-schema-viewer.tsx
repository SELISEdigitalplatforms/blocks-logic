import { DraggableArrayIndex, DraggableProperty } from "./draggable-property";
import { formatCellValue } from "../utils/format.util";
import type { FieldReferenceTarget, PathSegment } from "../utils/field-reference.util";

type RecursiveSchemaViewerProps = {
  data: unknown;
  depth?: number;
  segments?: PathSegment[];
  nodeName: string;
  isDirectParent: boolean;
  itemIndex?: number;
  itemIsObject?: boolean;
  target?: FieldReferenceTarget;
  showValues?: boolean;
  isDraggable?: boolean;
  showColon?: boolean;
};

export function RecursiveSchemaViewer({ data, depth = 0, segments = [], nodeName, isDirectParent, itemIndex = 0, itemIsObject = true, target, showValues = true, isDraggable = true, showColon = true }: RecursiveSchemaViewerProps) {
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
        const currentSegments = [...segments, isArray ? Number(key) : key];
        const childIsObj = typeof val === "object" && val !== null;

        return (
          <div key={key} className="flex flex-col">
            <div className="flex items-center gap-2">
              {!isArray ? (
                <DraggableProperty
                  segments={currentSegments}
                  depth={depth}
                  {...referenceProps}
                  label={key}
                  isDraggable={isDraggable}
                  showColon={showColon}
                />
              ) : (
                <DraggableArrayIndex
                  segments={currentSegments}
                  label={`[${key}]`}
                  depth={depth}
                  {...referenceProps}
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
                segments={currentSegments}
                {...referenceProps}
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
