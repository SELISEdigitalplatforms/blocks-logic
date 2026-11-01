import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { useT } from "@seliseblocks/genesis-os";

const LOGIC_SCHEDULER_MODULE = "logic.scheduler";

export const ExecutionSheetLoading = () => {
  const { t } = useT(LOGIC_SCHEDULER_MODULE);
  return (
    // <output> carries the "status" role natively.
    <output className="flex items-center gap-2 px-4 py-8 text-sm text-muted-foreground">
      <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
      {t("EXECUTION_SHEET_STATES.LOADING_EXECUTION")}
    </output>
  );
};

interface ExecutionSheetErrorProps {
  notFound?: boolean;
  onRetry: () => void;
}

export const ExecutionSheetError = ({ notFound = false, onRetry }: Readonly<ExecutionSheetErrorProps>) => {
  const { t } = useT(LOGIC_SCHEDULER_MODULE);
  return (
    <div className="space-y-3 px-4 py-8 text-sm" role="alert">
      <p className="text-foreground">
        {notFound ? t("EXECUTION_SHEET_STATES.THIS_EXECUTION_NO_LONGER") : t("EXECUTION_SHEET_STATES.COULDN_T_LOAD_THIS")}
      </p>
      {!notFound && (
        <Button type="button" variant="outline" size="xs" onClick={onRetry}>
          {t("common:RETRY")}
        </Button>
      )}
    </div>
  );
};

export const SnapshotUnavailable = () => {
  const { t } = useT(LOGIC_SCHEDULER_MODULE);
  return (
    <p className="text-xs text-muted-foreground">{t("EXECUTION_SHEET_STATES.SCHEDULE_SNAPSHOT_UNAVAILABLE_FOR")}</p>
  );
};
