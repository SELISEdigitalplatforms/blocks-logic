import { useRef, MouseEvent, useState, useEffect } from "react";
import { Search, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { cn, debounce } from "@/lib/utils";

interface SearchInputProps {
  onChange: (value: string) => void;
  placeholder?: string;
  value: string;
  className?: string;
  /** Accessible name. A placeholder alone is not a label — pass this when there is no visible one. */
  "aria-label"?: string;
}

export const SearchInput: React.FC<SearchInputProps> = ({
  onChange,
  placeholder = "Search...",
  value,
  className = "",
  "aria-label": ariaLabel,
}) => {
  const [state, setState] = useState(value);
  const [prevValue, setPrevValue] = useState(value);
  const inputRef = useRef<HTMLInputElement>(null);

  // Follow the controlled value when the parent changes it (a reset, a URL change), adjusting
  // during render rather than in an effect so there is no extra render with the stale text.
  if (value !== prevValue) {
    setPrevValue(value);
    setState(value);
  }

  // The handler travels with each call, so the debounced one runs the latest `onChange` a parent
  // passed instead of the first one being kept for the component's lifetime.
  const [debounced] = useState(() =>
    debounce((val: string, handler: (value: string) => void) => handler(val), 300),
  );

  useEffect(() => {
    return () => {
      debounced.cancel();
    };
  }, [debounced]);

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    event.stopPropagation();
    setState(event.target.value);
    debounced(event.target.value, onChange);
  };

  const handleClear = (e: MouseEvent) => {
    e.stopPropagation();
    // A search typed just before clearing would otherwise land after it and restore the text.
    debounced.cancel();
    setState("");
    onChange("");
  };

  return (
    <div className="flex items-center rounded-sm border px-2">
      <Search className="mr-2 h-4 w-4 text-muted-foreground" />
      <Input
        ref={inputRef}
        aria-label={ariaLabel}
        placeholder={placeholder}
        value={state}
        onChange={handleChange}
        className={cn(
          "h-8 w-52 border-none p-0 focus-visible:ring-0 focus-visible:ring-offset-0",
          className,
        )}
      />

      <Button
        variant="ghost"
        size="xs"
        className={cn("h-full p-1 pr-0 hover:bg-transparent", !value && "invisible")}
        onClick={handleClear}
      >
        <X className="h-4 w-4 text-muted-foreground" />
      </Button>
    </div>
  );
};
