import { Copy } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { useCopyToClipboard } from "@/hooks/use-copy-to-clipboard";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useT } from "@seliseblocks/genesis-os";

interface CopyIconButtonProps {
  value: string;
  /** What is copied, for the accessible name ("Copy {label}") and the toast. */
  label: string;
  className?: string;
}

export const CopyIconButton = ({ value, label, className }: Readonly<CopyIconButtonProps>) => {
  const { t } = useT("logic.scheduler");
  const { copy, isCopying } = useCopyToClipboard();

  const onCopy = () =>
    copy(
      value,
      () => showSuccessToast({ description: t("COPY_ICON_BUTTON.COPIED_TO_CLIPBOARD", { label }) }),
      () =>
        showErrorToast({
          errors: t("COPY_ICON_BUTTON.COULDN_T_COPY_THE", { value: label.toLowerCase() }),
        }),
    );

  return (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      className={cn("h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground", className)}
      aria-label={t("COPY_ICON_BUTTON.COPY", { value: label.toLowerCase() })}
      disabled={isCopying}
      onClick={onCopy}
    >
      <Copy className="h-3.5 w-3.5" aria-hidden />
    </Button>
  );
};
