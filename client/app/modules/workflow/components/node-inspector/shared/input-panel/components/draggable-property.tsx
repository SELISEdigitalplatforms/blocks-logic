import {
  buildFieldReference,
  EXPRESSION_TARGET,
  type FieldReferenceTarget,
  type PathSegment,
} from "../utils/field-reference.util";

type DraggableReferenceProps = Readonly<{
  segments: PathSegment[];
  label?: string;
  depth?: number;
  nodeName: string;
  isDirectParent: boolean;
  itemIndex?: number;
  itemIsObject?: boolean;
  target?: FieldReferenceTarget;
  isDraggable?: boolean;
  showColon?: boolean;
}>;

function getReference({
  segments,
  nodeName,
  isDirectParent,
  itemIndex = 0,
  itemIsObject = true,
  target = EXPRESSION_TARGET,
}: DraggableReferenceProps): string {
  return buildFieldReference(
    { segments, nodeName, isDirectParent, itemIndex, itemIsObject },
    target,
  );
}

export function DraggableProperty({
  segments,
  label,
  depth = 0,
  nodeName,
  isDirectParent,
  itemIndex,
  itemIsObject,
  target,
  isDraggable = true,
  showColon = true,
}: DraggableReferenceProps) {
  const expression = getReference({
    segments,
    nodeName,
    isDirectParent,
    itemIndex,
    itemIsObject,
    target,
  });
  const lastSegment = segments.at(-1);
  const fallbackLabel = lastSegment === undefined ? "output" : String(lastSegment);
  const displayLabel = label ?? fallbackLabel;

  return (
    <button
      type="button"
      draggable={isDraggable}
      disabled={!isDraggable}
      onDragStart={(e) => {
        if (!isDraggable) return;
        e.dataTransfer.setData("text/plain", expression);
        e.dataTransfer.effectAllowed = "copy";
      }}
      className={`group flex items-center gap-2 rounded px-2 py-1 text-xs touch-none select-none disabled:cursor-default disabled:opacity-100 ${isDraggable ? "cursor-pointer hover:bg-surface-hover" : ""} `}
      style={{ marginLeft: `${depth * 1}rem` }}
      title={isDraggable ? `Drag to use: ${expression}` : undefined}
    >
      <span
        className={`font-mono flex items-center ${isDraggable ? "cursor-grab active:cursor-grabbing text-high-emphasis" : "text-medium-emphasis"}`}
      >
        <span
          className={`rounded-md border border-border/80 px-1.5 py-0.5 mr-0.5 shadow-sm ${isDraggable && "bg-white dark:bg-gray-800"}`}
        >
          {displayLabel}
        </span>
        {showColon && ":"}
      </span>
    </button>
  );
}

export function DraggableArrayIndex({
  segments,
  label,
  depth = 0,
  nodeName,
  isDirectParent,
  itemIndex,
  itemIsObject,
  target,
  isDraggable = true,
  showColon = true,
}: DraggableReferenceProps) {
  const expression = getReference({
    segments,
    nodeName,
    isDirectParent,
    itemIndex,
    itemIsObject,
    target,
  });
  const lastSegment = segments.at(-1);

  return (
    <button
      type="button"
      draggable={isDraggable}
      disabled={!isDraggable}
      onDragStart={(e) => {
        if (!isDraggable) return;
        e.dataTransfer.setData("text/plain", expression);
        e.dataTransfer.effectAllowed = "copy";
      }}
      className={`group flex items-center gap-2 rounded px-2 py-1 text-xs touch-none select-none disabled:cursor-default disabled:opacity-100 ${isDraggable ? "cursor-pointer hover:bg-surface-hover" : ""} `}
      style={{ marginLeft: `${depth * 1}rem` }}
      title={isDraggable ? `Drag to use: ${expression}` : undefined}
    >
      <span
        className={`font-mono flex items-center ${isDraggable ? "cursor-grab active:cursor-grabbing text-high-emphasis" : "text-medium-emphasis"}`}
      >
        <span
          className={`rounded-md border border-border/80 px-1.5 py-0.5 mr-0.5 shadow-sm ${isDraggable && "bg-white dark:bg-gray-800"}`}
        >
          {label || `[${lastSegment ?? 0}]`}
        </span>
        {showColon && ":"}
      </span>
    </button>
  );
}
