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
import { usePublishNewWorkflow, usePublishWorkflow, useUnpublishWorkflow } from "../../hooks/use-workflow-api";
import { PublishConfirmationModal, UnpublishConfirmationModal } from "../workflow-confirmation-modals";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

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
  
  const [isPublishDialogOpen, setIsPublishDialogOpen] = useState(false);
  const [isPublishConfirmOpen, setIsPublishConfirmOpen] = useState(false);
  const [isUnpublishDialogOpen, setIsUnpublishDialogOpen] = useState(false);
  const [publishVersionName, setPublishVersionName] = useState("");
  const [publishDescription, setPublishDescription] = useState("");

  const { mutateAsync: publishNewWorkflow, isPending: isPublishingNew } = usePublishNewWorkflow();
  const { mutateAsync: publishWorkflow, isPending: isPublishingUnversioned } = usePublishWorkflow();
  const { mutateAsync: unpublishWorkflow, isPending: isUnpublishing } = useUnpublishWorkflow();

  const handleOpenPublishDialog = () => {
    if (!isPublished && !isDirty) {
      setIsPublishConfirmOpen(true);
    } else {
      const id = Math.random().toString(16).substring(2, 10);
      setPublishVersionName(`Version ${id}`);
      setPublishDescription("");
      setIsPublishDialogOpen(true);
    }
  };

  const handlePublish = async () => {
    if (!workflowId) return;
    try {
      await publishNewWorkflow({ 
        workflowId, 
        name: publishVersionName, 
        description: publishDescription 
      });
      setIsPublishDialogOpen(false);
      onActionComplete?.();
      showSuccessToast({ description: "Workflow successfully published." });
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to publish workflow." });
    }
  };

  const handlePublishUnversioned = async () => {
    if (!workflowId) return;
    try {
      await publishWorkflow({ workflowId });
      setIsPublishConfirmOpen(false);
      onActionComplete?.();
      showSuccessToast({ description: "Workflow successfully published." });
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to publish workflow." });
    }
  };

  const handleUnpublish = async () => {
    if (!workflowId) return;
    try {
      await unpublishWorkflow({ workflowId });
      setIsUnpublishDialogOpen(false);
      onActionComplete?.();
      showSuccessToast({ description: "Workflow successfully unpublished." });
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to unpublish workflow." });
    }
  };

  const isPending = isPublishingNew || isPublishingUnversioned || isUnpublishing;


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
          <DropdownMenuItem onClick={handleOpenPublishDialog} disabled={hasUnsavedChanges || (isPublished && !isDirty)}>
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
        isPublishing={isPublishingNew}
      />

      <PublishConfirmationModal
        open={isPublishConfirmOpen}
        onOpenChange={setIsPublishConfirmOpen}
        onConfirm={handlePublishUnversioned}
        isPending={isPublishingUnversioned}
      />

      <UnpublishConfirmationModal
        open={isUnpublishDialogOpen}
        onOpenChange={setIsUnpublishDialogOpen}
        onConfirm={handleUnpublish}
        isPending={isUnpublishing}
      />
    </>
  );
};


