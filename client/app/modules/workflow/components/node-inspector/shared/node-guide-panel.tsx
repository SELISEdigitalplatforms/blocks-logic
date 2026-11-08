import { Button } from "@/components/ui-kits/button/button";
import { X } from "lucide-react";
import type { ComponentType } from "react";
import { useT } from "@seliseblocks/genesis-os";

type NodeGuidePanelProps = {
  guide: ComponentType;
  onClose: () => void;
};

export const NodeGuidePanel = ({ guide: Guide, onClose }: NodeGuidePanelProps) => {
  const { t } = useT("logic.workflow.nodes.inspector");
  return (
    <div className="flex h-full min-h-0 flex-col overflow-hidden rounded border bg-background">
      <div className="flex shrink-0 items-center justify-between border-b px-4 py-3">
        <h3 className="text-base font-semibold">{t("NODE_GUIDE_PANEL.GUIDE")}</h3>
        <Button
          variant="ghost"
          size="sm"
          className="h-fit w-fit p-1"
          onClick={onClose}
          aria-label={t("NODE_GUIDE_PANEL.HIDE_GUIDE")}
        >
          <X className="h-5 w-5" />
        </Button>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto p-4">
        <Guide />
      </div>
    </div>
  );
};
