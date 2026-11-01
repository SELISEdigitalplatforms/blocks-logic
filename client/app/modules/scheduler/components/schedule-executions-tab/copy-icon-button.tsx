import { Copy } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { useCopyToClipboard } from "@/hooks/use-copy-to-clipboard";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

interface CopyIconButtonProps {
  value: string;
  /** What is copied, for the accessible name ("Copy {label}") and the toast. */
  label: string;
  className?: string;
}

export const CopyIconButton = ({ value, label, className }: Readonly<CopyIconButtonProps>) => {
  const { copy, isCopying } = useCopyToClipboard();

  const onCopy = () =>
    copy(
      value,
      () => showSuccessToast({ description: `${label} copied to clipboard.` }),
      () => showErrorToast({ errors: `Couldn't copy the ${label.toLowerCase()}.` }),
    );

  return (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      className={cn("h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground", className)}
      aria-label={`Copy ${label.toLowerCase()}`}
      disabled={isCopying}
      onClick={onCopy}
    >
      <Copy className="h-3.5 w-3.5" aria-hidden />
    </Button>
  );
};
