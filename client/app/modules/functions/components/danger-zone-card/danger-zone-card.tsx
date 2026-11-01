import { Button } from "@/components/ui-kits/button/button";
import { Trash2 } from "lucide-react";
import { useT } from "@seliseblocks/genesis-os";

type DangerZoneCardProps = {
  onDelete: () => void;
};

export const DangerZoneCard = ({ onDelete }: DangerZoneCardProps) => {
  const { t } = useT("logic.functions.components");
  return (
    <div className="flex flex-wrap items-center justify-between gap-4 rounded-lg border border-error/30 bg-error/5 p-4">
      <div className="flex flex-col gap-0.5">
        <span className="text-sm font-semibold text-error">
          {t("DANGER_ZONE_CARD.DANGER_ZONE")}
        </span>
        <span className="text-xs text-medium-emphasis">
          {t("DANGER_ZONE_CARD.DELETING_THE_FUNCTION_TAKES")}
        </span>
      </div>
      <Button variant="destructive-outline" size="sm" className="gap-1.5" onClick={onDelete}>
        <Trash2 className="h-3.5 w-3.5" />
        {t("DANGER_ZONE_CARD.DELETE_FUNCTION")}
      </Button>
    </div>
  );
};
