import { Button } from "@/components/ui-kits/button/button";
import { Trash2 } from "lucide-react";

type DangerZoneCardProps = {
  onDelete: () => void;
};

export const DangerZoneCard = ({ onDelete }: DangerZoneCardProps) => (
  <div className="flex flex-wrap items-center justify-between gap-4 rounded-lg border border-error/30 bg-error/5 p-4">
    <div className="flex flex-col gap-0.5">
      <span className="text-sm font-semibold text-error">Danger zone</span>
      <span className="text-xs text-medium-emphasis">
        Deleting the function takes its endpoint, versions, runs and logs with it. Callers start
        getting a 404 immediately.
      </span>
    </div>
    <Button variant="destructive-outline" size="sm" className="gap-1.5" onClick={onDelete}>
      <Trash2 className="h-3.5 w-3.5" />
      Delete function
    </Button>
  </div>
);
