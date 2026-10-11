import { useRef, type KeyboardEvent, type PointerEvent, type RefObject } from "react";
import { GripHorizontal } from "lucide-react";
import { cn } from "@/lib/utils";

/** One arrow press. Shift moves in bigger steps, as it does in most splitters. */
const KEY_STEP = 20;
const KEY_STEP_LARGE = 100;

type ResizeHandleProps = {
  /** The element being resized. Its rendered height is where a drag or a key press starts from. */
  targetRef: RefObject<HTMLElement | null>;
  /** The current explicit height in px, or `null` while the target still sits at its default. */
  height: number | null;
  min: number;
  max: number;
  /** Always called with a whole number already clamped to `[min, max]`. */
  onResize: (height: number) => void;
  /** Double-click: drop the explicit height and go back to the default. */
  onReset: () => void;
  label: string;
  className?: string;
};

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

/**
 * A bottom-edge grip that makes the element above it taller or shorter. Pointer drag with capture,
 * so the drag keeps going when the pointer leaves the thin bar or passes over the editor; arrow
 * keys for anyone not using a mouse; double-click to reset. The height never leaves `[min, max]`.
 */
export const ResizeHandle = ({
  targetRef,
  height,
  min,
  max,
  onResize,
  onReset,
  label,
  className,
}: ResizeHandleProps) => {
  const drag = useRef<{ pointerId: number; startY: number; startHeight: number } | null>(null);

  // The rendered height, not `height`: while the target is at its default `height` is null, and
  // the default is a CSS expression only the browser can resolve.
  const currentHeight = () => {
    const rendered = targetRef.current?.getBoundingClientRect().height;
    return rendered && Number.isFinite(rendered) ? rendered : (height ?? min);
  };

  const resizeTo = (next: number) => {
    if (!Number.isFinite(next)) return;
    onResize(Math.round(clamp(next, min, max)));
  };

  const handlePointerDown = (event: PointerEvent<HTMLDivElement>) => {
    // Primary button only, and one drag at a time (a second finger must not restart it).
    if (event.button !== 0 || drag.current) return;
    event.preventDefault(); // no text selection while dragging
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = {
      pointerId: event.pointerId,
      startY: event.clientY,
      startHeight: currentHeight(),
    };
  };

  const handlePointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const active = drag.current;
    if (!active || active.pointerId !== event.pointerId) return;
    resizeTo(active.startHeight + event.clientY - active.startY);
  };

  const endDrag = (event: PointerEvent<HTMLDivElement>) => {
    if (drag.current?.pointerId !== event.pointerId) return;
    drag.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const step = event.shiftKey ? KEY_STEP_LARGE : KEY_STEP;
    const next = {
      ArrowDown: currentHeight() + step,
      ArrowUp: currentHeight() - step,
      End: max,
      Home: min,
    }[event.key];
    if (next === undefined) return;
    event.preventDefault();
    resizeTo(next);
  };

  return (
    <div
      role="separator"
      aria-orientation="horizontal"
      aria-label={label}
      aria-valuemin={min}
      aria-valuemax={max}
      // Only an explicit height is known at render; the default is resolved by the browser.
      aria-valuenow={height ?? undefined}
      tabIndex={0}
      title="Drag to resize · double-click to reset"
      className={cn(
        "flex h-3 shrink-0 cursor-row-resize touch-none select-none items-center justify-center",
        "text-low-emphasis hover:bg-surface-app hover:text-foreground",
        "focus-visible:bg-surface-app focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-primary",
        className,
      )}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onLostPointerCapture={endDrag}
      onKeyDown={handleKeyDown}
      onDoubleClick={onReset}
    >
      <GripHorizontal className="h-3 w-3" aria-hidden />
    </div>
  );
};
