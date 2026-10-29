import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";

export const ExecutionSheetLoading = () => (
  // <output> carries the "status" role natively.
  <output className="flex items-center gap-2 px-4 py-8 text-sm text-muted-foreground">
    <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
    Loading execution…
  </output>
);

interface ExecutionSheetErrorProps {
  notFound?: boolean;
  onRetry: () => void;
}

export const ExecutionSheetError = ({ notFound = false, onRetry }: Readonly<ExecutionSheetErrorProps>) => (
  <div className="space-y-3 px-4 py-8 text-sm" role="alert">
    <p className="text-foreground">
      {notFound ? "This execution no longer exists." : "Couldn't load this execution."}
    </p>
    {!notFound && (
      <Button type="button" variant="outline" size="xs" onClick={onRetry}>
        Retry
      </Button>
    )}
  </div>
);

export const SnapshotUnavailable = () => (
  <p className="text-xs text-muted-foreground">Schedule snapshot unavailable for this run.</p>
);
