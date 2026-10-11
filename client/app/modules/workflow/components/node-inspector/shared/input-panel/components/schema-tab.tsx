import { RecursiveSchemaViewer } from "./recursive-schema-viewer";
import { isPlainObject, type FieldReferenceTarget } from "../utils/field-reference.util";

type SchemaTabProps = Readonly<{
  runtimeInputRows: unknown[];
  isLastExecutionEditor: boolean;
  nodeName: string;
  isDirectParent: boolean;
  target?: FieldReferenceTarget;
  isExecutionMode: boolean;
}>;

const getRowKey = (row: unknown) => {
  try {
    return typeof row === "object" && row !== null ? JSON.stringify(row) : String(row);
  } catch {
    return Object.prototype.toString.call(row);
  }
};

export function SchemaTab({
  runtimeInputRows,
  isLastExecutionEditor,
  nodeName,
  isDirectParent,
  target,
  isExecutionMode,
}: SchemaTabProps) {
  if (runtimeInputRows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime input schema available.</p>;
  }

  return (
    <div className="flex flex-col gap-1">
      {isLastExecutionEditor && (
        <div className="rounded border border-border/80 bg-surface-app p-2">
          <p className="text-xs text-medium-emphasis">
            The fields below come from last successful execution. Execute Node to refresh them.
          </p>
        </div>
      )}
      {runtimeInputRows.map((row, index) => (
        <div key={`schema-row-${getRowKey(row)}`} className="rounded border border-border/60 p-2">
          <p className="mb-1 text-xs font-semibold text-medium-emphasis">item {index + 1}:</p>
          <RecursiveSchemaViewer
            data={row}
            nodeName={nodeName}
            isDirectParent={isDirectParent}
            itemIndex={index}
            itemIsObject={isPlainObject(row)}
            target={target}
            showValues={!isLastExecutionEditor}
            isDraggable={!isExecutionMode}
            showColon={!isLastExecutionEditor}
          />
        </div>
      ))}
    </div>
  );
}
