import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Label } from "@/components/ui-kits/label/label";
import { Loader2 } from "lucide-react";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

interface PublishWorkflowModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  publishVersionName: string;
  setPublishVersionName: (name: string) => void;
  publishDescription: string;
  setPublishDescription: (desc: string) => void;
  onPublish: () => void;
  isPublishing: boolean;
  mode?: "publish" | "edit";
}

export const PublishWorkflowModal = ({
  open,
  onOpenChange,
  publishVersionName,
  setPublishVersionName,
  publishDescription,
  setPublishDescription,
  onPublish,
  isPublishing,
  mode = "publish",
}: PublishWorkflowModalProps) => {
  const { t } = useT("logic.workflow");
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>{mode === "publish" ? t("PUBLISH_WORKFLOW_MODAL.PUBLISH_WORKFLOW") : t("PUBLISH_WORKFLOW_MODAL.EDIT_VERSION_DETAILS")}</DialogTitle>
        </DialogHeader>
        <div className="grid gap-4 py-4">
          <div className="grid gap-2">
            <Label htmlFor="version-name">
              {renderRichText(t("PUBLISH_WORKFLOW_MODAL.VERSION_NAME_0_0"), [(children) => <span className="text-red-500">{children}</span>])}
            </Label>
            <Input
              id="version-name"
              value={publishVersionName}
              maxLength={50}
              onChange={(e) => setPublishVersionName(e.target.value)}
            />
          </div>
          <div className="grid gap-2">
            <Label htmlFor="description">{t("PUBLISH_WORKFLOW_MODAL.DESCRIBE_CHANGES_OPTIONAL")}</Label>
            <Textarea
              id="description"
              value={publishDescription}
              maxLength={255}
              onChange={(e) => setPublishDescription(e.target.value)}
              rows={4}
            />
          </div>
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => onOpenChange(false)}>
            {t("common:CANCEL")}
          </Button>
          <Button
            onClick={onPublish}
            disabled={!publishVersionName.trim() || isPublishing}
          >
            {isPublishing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            {mode === "publish" ? t("PUBLISH_WORKFLOW_MODAL.PUBLISH") : t("common:SAVE_CHANGES")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
