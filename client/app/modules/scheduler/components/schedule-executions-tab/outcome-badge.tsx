import { CheckCircle2, XCircle } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { ScheduleExecutionOutcome } from "../../types/schedule.service.type";
import { useT } from "@seliseblocks/genesis-os";

/** Icon and text, never colour alone; dark-aware tokens only. */
export const OutcomeBadge = ({ outcome }: Readonly<{ outcome: ScheduleExecutionOutcome }>) => {
  const { t } = useT("logic.scheduler");
  if (outcome === "Succeeded") {
    return (
      <Badge variant="outline" className="w-fit gap-1.5 text-foreground">
        <CheckCircle2 className="h-3.5 w-3.5 text-success" aria-hidden />
        {t("OUTCOME_BADGE.SUCCEEDED")}
      </Badge>
    );
  }

  return (
    <Badge className="w-fit gap-1.5 border-transparent bg-blocks-error-100 text-blocks-error-800 hover:bg-blocks-error-100">
      <XCircle className="h-3.5 w-3.5" aria-hidden />
      {t("OUTCOME_BADGE.FAILED")}
    </Badge>
  );
};
