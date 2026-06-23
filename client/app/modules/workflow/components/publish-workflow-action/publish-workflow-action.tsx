import { useState } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { ChevronDown, Loader2 } from "lucide-react";
import { useParams } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { usePublishWorkflow, useUnpublishWorkflow } from "../../hooks/use-workflow-api";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Label } from "@/components/ui-kits/label/label";

interface PublishWorkflowActionProps {
  isDirty?: boolean;
  hasUnsavedChanges?: boolean;
  isPublished?: boolean;
  onActionComplete?: () => void;
}

export const PublishWorkflowAction = ({
  isDirty,
  hasUnsavedChanges,
  isPublished,
  onActionComplete,
}: PublishWorkflowActionProps) => {
  const { id: workflowId } = useParams<{ id: string }>();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  
  const [isPublishDialogOpen, setIsPublishDialogOpen] = useState(false);
  const [publishVersionName, setPublishVersionName] = useState("");
  const [publishDescription, setPublishDescription] = useState("");

  const { mutateAsync: publishWorkflow, isPending: isPublishing } = usePublishWorkflow();
  const { mutateAsync: unpublishWorkflow, isPending: isUnpublishing } = useUnpublishWorkflow();

  const handleOpenPublishDialog = () => {
    const id = Math.random().toString(16).substring(2, 10);
    setPublishVersionName(`Version ${id}`);
    setPublishDescription("");
    setIsPublishDialogOpen(true);
  };

  const handlePublish = async () => {
    if (!workflowId) return;
    try {
      await publishWorkflow({ 
        projectKey, 
        workflowId, 
        name: publishVersionName, 
        Description: publishDescription 
      });
      setIsPublishDialogOpen(false);
      onActionComplete?.();
    } catch (error) {
      console.error(error);
    }
  };

  const handleUnpublish = async () => {
    if (!workflowId) return;
    try {
      await unpublishWorkflow({ projectKey, workflowId });
      onActionComplete?.();
    } catch (error) {
      console.error(error);
    }
  };

  const isPending = isPublishing || isUnpublishing;

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" className="gap-2" disabled={isPending || hasUnsavedChanges}>
            {isPending && <Loader2 className="h-4 w-4 animate-spin" />}
            Publish
            <ChevronDown className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onClick={handleOpenPublishDialog} disabled={!isDirty || hasUnsavedChanges}>
            Publish
          </DropdownMenuItem>
          <DropdownMenuItem onClick={handleUnpublish} disabled={!isPublished }>
            Unpublish
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={isPublishDialogOpen} onOpenChange={setIsPublishDialogOpen}>
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
                onChange={(e) => setPublishDescription(e.target.value)}
                rows={4}
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setIsPublishDialogOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={handlePublish}
              disabled={!publishVersionName.trim() || isPublishing}
            >
              {isPublishing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
              Publish
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
};

