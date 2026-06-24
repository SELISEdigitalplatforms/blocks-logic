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
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";

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
  const [isUnpublishDialogOpen, setIsUnpublishDialogOpen] = useState(false);
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
      setIsUnpublishDialogOpen(false);
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
          <DropdownMenuItem onClick={() => setIsUnpublishDialogOpen(true)} disabled={!isPublished}>
            Unpublish
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <PublishWorkflowModal
        open={isPublishDialogOpen}
        onOpenChange={setIsPublishDialogOpen}
        publishVersionName={publishVersionName}
        setPublishVersionName={setPublishVersionName}
        publishDescription={publishDescription}
        setPublishDescription={setPublishDescription}
        onPublish={handlePublish}
        isPublishing={isPublishing}
      />

      <Dialog open={isUnpublishDialogOpen} onOpenChange={setIsUnpublishDialogOpen}>
        <ConfirmationModal
          data={{
            dialogTitle: "Unpublish workflow",
            dialogSubtitle: "Are you sure you want to unpublish this workflow? It will no longer be available for execution.",
            confirmButton: "Unpublish",
          }}
          onConfirm={handleUnpublish}
          onCancel={() => setIsUnpublishDialogOpen(false)}
          buttonState={{ confirm: { disable: isUnpublishing } }}
        />
      </Dialog>
    </>
  );
};


