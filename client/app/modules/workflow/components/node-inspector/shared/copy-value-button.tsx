import { useEffect, useRef, useState } from "react";
import { Check, Copy } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { copyToClipboard } from "@blocks-workflow/utils/copy-to-clipboard";

const COPIED_RESET_MS = 1500;

type CopyValueButtonProps = Readonly<{
  value: string;
  label: string;
  className?: string;
  iconClassName?: string;
}>;

/**
 * Icon-only copy action. Swaps to a check once the value is on the clipboard, then back to the copy
 * icon so it can be clicked again.
 */
export function CopyValueButton({ value, label, className, iconClassName = "h-4 w-4" }: CopyValueButtonProps) {
  const [copied, setCopied] = useState(false);
  const resetTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => () => clearTimeout(resetTimer.current), []);

  const handleClick = async (e: React.MouseEvent) => {
    e.stopPropagation();
    if (!(await copyToClipboard(value))) return;
    setCopied(true);
    clearTimeout(resetTimer.current);
    resetTimer.current = setTimeout(() => setCopied(false), COPIED_RESET_MS);
  };

  const title = copied ? "Copied" : label;

  return (
    <Button
      type="button"
      variant="ghost"
      size="xxs"
      className={cn("h-7 shrink-0 px-2 text-low-emphasis", className)}
      aria-label={label}
      title={title}
      onClick={handleClick}
    >
      {copied ? (
        <Check className={cn(iconClassName, "text-green-600")} aria-hidden="true" />
      ) : (
        <Copy className={iconClassName} aria-hidden="true" />
      )}
    </Button>
  );
}
