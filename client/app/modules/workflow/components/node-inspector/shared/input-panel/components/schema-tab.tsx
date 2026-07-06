import { RecursiveSchemaViewer } from "./recursive-schema-viewer";

export function SchemaTab({ 
  runtimeInputRows, 
  isLastExecutionEditor, 
  nodeName, 
  hasSinglePredecessor, 
  isExecutionMode 
}: { 
  runtimeInputRows: unknown[];
  isLastExecutionEditor: boolean;
  nodeName: string;
  hasSinglePredecessor: boolean;
  isExecutionMode: boolean;
}) {
  if (runtimeInputRows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime input schema available.</p>;
  }

  return (
    <div className="flex flex-col gap-1">
      {isLastExecutionEditor && (
        <div className="rounded border border-border/80 bg-surface-app p-2">
          <p className="text-xs text-medium-emphasis">The fields below come from last successful execution. Execute Node to refresh them.</p>
        </div>
      )}
      {runtimeInputRows.map((row, index) => (
        <div key={index} className="rounded border border-border/60 p-2">
          <p className="mb-1 text-xs font-semibold text-medium-emphasis">item {index + 1}:</p>
          <RecursiveSchemaViewer 
            data={row} 
            nodeName={nodeName} 
            hasSinglePredecessor={hasSinglePredecessor}
            showValues={!isLastExecutionEditor}
            isDraggable={!isExecutionMode}
            isDashed={isLastExecutionEditor}
          />
        </div>
      ))}
    </div>
  );
}
