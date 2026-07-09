type DraggablePropertyProps = {
  fieldKey?: string,
  prefixPath?: string,
  label?: string,
  depth?: number,
  nodeName: string,
  hasSinglePredecessor: boolean,
  isRoot?: boolean,
  isDraggable?: boolean;
  showColon?: boolean;
}
export function DraggableProperty({ fieldKey, prefixPath = "", label, depth = 0, nodeName, hasSinglePredecessor = true, isRoot = false, isDraggable = true, showColon = true }: DraggablePropertyProps) {
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
      className={`group flex items-center gap-2 rounded px-2 py-1 text-xs touch-none select-none ${isDraggable ? 'cursor-pointer hover:bg-surface-hover' : ''} `}
      style={{ marginLeft: `${depth * 1}rem` }}
      title={isDraggable ? `Drag to use: ${expression}` : undefined}
    >
      <span className={`font-mono flex items-center ${isDraggable ? 'cursor-grab active:cursor-grabbing text-high-emphasis' : 'text-medium-emphasis'}`}>
        <span className={`rounded-md border border-border/80 px-1.5 py-0.5 mr-0.5 shadow-sm ${isDraggable && "bg-white dark:bg-gray-800"}`}>
          {label || fieldKey || "output"}
        </span>
        {showColon && ":"}
      </span>
    </div>
  );
}
