import { useRef } from "react";

export function useCursorTracker() {
  const selectionRef = useRef<{ start: number; end: number } | null>(null);

  const updatePosition = (e: React.SyntheticEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    if (e.type === "blur") return;

    const target = e.currentTarget;
    if (typeof target.selectionStart === "number") {
      selectionRef.current = {
        start: target.selectionStart,
        end: target.selectionEnd || target.selectionStart,
      };
    }
  };

  const insertAtCursor = (currentValue: string, textToInsert: string) => {
    let start = currentValue.length;
    let end = currentValue.length;
    if (selectionRef.current) {
      // Ensure start and end are within bounds of the current value
      start = Math.min(selectionRef.current.start, currentValue.length);
      end = Math.min(selectionRef.current.end, currentValue.length);
    }
    
    const newValue = currentValue.substring(0, start) + textToInsert + currentValue.substring(end);
    
    // Advance the saved cursor position to the end of the inserted text
    // This allows multiple consecutive drops to place text sequentially
    const newPos = start + textToInsert.length;
    selectionRef.current = { start: newPos, end: newPos };
    
    return newValue;
  };

  return {
    selectionRef,
    updatePosition,
    insertAtCursor,
  };
}
