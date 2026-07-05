export function DraggableProperty({ fieldKey, prefixPath = "", label, depth = 0, nodeName, hasSinglePredecessor, isRoot = false, isDraggable = true, isDashed = false }: { fieldKey?: string; prefixPath?: string; label?: string; depth?: number; nodeName: string; hasSinglePredecessor: boolean; isRoot?: boolean; isDraggable?: boolean; isDashed?: boolean }) {
  const expressionPath = prefixPath ? (fieldKey ? `${prefixPath}.${fieldKey}` : prefixPath) : (fieldKey || "");
  
  const expression = hasSinglePredecessor
    ? `{{$json.output${expressionPath ? '.' + expressionPath : ''}}}`
    : `{{$node["${nodeName}"].json.output${expressionPath ? '.' + expressionPath : ''}}}`;

  return (
    <div
      draggable={isDraggable}
      onDragStart={(e) => {
        if (!isDraggable) return;
        e.dataTransfer.setData("text/plain", expression);
        e.dataTransfer.dropEffect = "copy";
      }}
      className={`group flex items-center gap-2 rounded px-2 py-1 text-xs touch-none select-none ${isDraggable ? 'cursor-pointer hover:bg-surface-hover' : ''} ${isDashed ? 'border border-dashed border-border/80 text-medium-emphasis bg-surface-app' : ''}`}
      style={{ marginLeft: `${depth * 1}rem` }}
      title={isDraggable ? `Drag to use: ${expression}` : undefined}
    >
      <span className={`font-mono ${isDraggable ? 'cursor-grab active:cursor-grabbing text-high-emphasis' : 'text-medium-emphasis'}`}>
        {label || fieldKey || "output"}{!isDashed &&":"}</span>
    </div>
  );
}
