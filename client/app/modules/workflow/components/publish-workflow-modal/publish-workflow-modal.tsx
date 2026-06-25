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

interface PublishWorkflowModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  publishVersionName: string;
  setPublishVersionName: (name: string) => void;
  publishDescription: string;
  setPublishDescription: (desc: string) => void;
  onPublish: () => void;
  isPublishing: boolean;
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
}: PublishWorkflowModalProps) => {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Publish workflow</DialogTitle>
        </DialogHeader>
        <div className="grid gap-4 py-4">
          <div className="grid gap-2">
            <Label htmlFor="version-name">
              Version name <span className="text-red-500">*</span>
            </Label>
            <Input
              id="version-name"
              value={publishVersionName}
              onChange={(e) => setPublishVersionName(e.target.value)}
            />
          </div>
          <div className="grid gap-2">
            <Label htmlFor="description">Describe changes (optional)</Label>
            <Textarea
              id="description"
              value={publishDescription}
              maxLength={100}
              onChange={(e) => setPublishDescription(e.target.value)}
              rows={4}
            />
          </div>
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={onPublish}
            disabled={!publishVersionName.trim() || isPublishing}
          >
            {isPublishing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Publish
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
