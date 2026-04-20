import { Button } from "@/components/ui-kits/button/button";
import { Shield, ShieldCheck, Trash2, X } from "lucide-react";
import { cn } from "@/lib/utils";

type BulkActionBarProps = {
  selectedCount: number;
  onEnableMfa: () => void;
  onEnableCaptcha: () => void;
  onRemove: () => void;
  onClear: () => void;
};

export const BulkActionBar = ({
  selectedCount,
  onEnableMfa,
  onEnableCaptcha,
  onRemove,
  onClear,
}: BulkActionBarProps) => {
  return (
    <div
      className={cn(
        "fixed bottom-6 left-1/2 z-50 -translate-x-1/2 transition-all duration-300",
        selectedCount > 0
          ? "translate-y-0 opacity-100"
          : "pointer-events-none translate-y-4 opacity-0",
      )}
    >
      <div className="flex items-center gap-3 rounded-xl border border-border bg-card px-5 py-3 shadow-lg">
        <div className="flex items-center gap-2 text-sm font-semibold">
          <span className="flex h-6 w-6 items-center justify-center rounded-full bg-primary text-xs text-primary-foreground">
            {selectedCount}
          </span>
          <span className="uppercase tracking-wider text-muted-foreground">
            Endpoint{selectedCount !== 1 ? "s" : ""} Selected
          </span>
        </div>

        <div className="mx-2 h-6 w-px bg-border" />

        <Button variant="ghost" size="sm" className="gap-1.5" onClick={onEnableMfa}>
          <ShieldCheck className="h-4 w-4" />
          Enable MFA
        </Button>

        <Button variant="ghost" size="sm" className="gap-1.5" onClick={onEnableCaptcha}>
          <Shield className="h-4 w-4" />
          Enable Captcha
        </Button>

        <Button
          variant="ghost"
          size="sm"
          className="gap-1.5 text-destructive hover:text-destructive"
          onClick={onRemove}
        >
          <Trash2 className="h-4 w-4" />
          Remove
        </Button>

        <div className="mx-1 h-6 w-px bg-border" />

        <button
          className="rounded-md p-1 hover:bg-accent"
          onClick={onClear}
        >
          <X className="h-4 w-4 text-muted-foreground" />
        </button>
      </div>
    </div>
  );
};
