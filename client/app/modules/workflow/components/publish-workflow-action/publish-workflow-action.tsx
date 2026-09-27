import { useState } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { ChevronDown, Loader2 } from "lucide-react";
import { useParams } from "react-router";
import { useWorkflowActions } from "../../hooks/use-workflow-actions";
import {
  PublishConfirmationModal,
  UnpublishConfirmationModal,
} from "../workflow-confirmation-modals";
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

  const [isPublishDialogOpen, setIsPublishDialogOpen] = useState(false);
  const [isPublishConfirmOpen, setIsPublishConfirmOpen] = useState(false);
  const [isUnpublishDialogOpen, setIsUnpublishDialogOpen] = useState(false);
  const [publishVersionName, setPublishVersionName] = useState("");
  const [publishDescription, setPublishDescription] = useState("");

  const {
    handlePublishNew,
    handlePublishUnversioned: publishUnversioned,
    handleUnpublish: unpublish,
    isPublishingNew,
    isPublishingUnversioned,
    isUnpublishing,
  } = useWorkflowActions();

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

  const handlePublish = () => {
    if (!workflowId) return;
    handlePublishNew(workflowId, publishVersionName, publishDescription, () => {
      setIsPublishDialogOpen(false);
      onActionComplete?.();
    });
  };

  const handlePublishUnversioned = () => {
    if (!workflowId) return;
    publishUnversioned(workflowId, undefined, () => {
      setIsPublishConfirmOpen(false);
      onActionComplete?.();
    });
  };

  const handleUnpublish = () => {
    if (!workflowId) return;
    unpublish(workflowId, () => {
      setIsUnpublishDialogOpen(false);
      onActionComplete?.();
    });
  };

  const isPending = isPublishingNew || isPublishingUnversioned || isUnpublishing;
  const isPublishedAndClean = isPublished && !isDirty;
  const isPublishDisabled = isPending || hasUnsavedChanges || isPublishedAndClean;
  const isDropdownDisabled = isPending || hasUnsavedChanges;

  return (
    <>
      <DropdownMenu>
        <div className="inline-flex overflow-hidden rounded-sm border border-input bg-background">
          <Button
            variant="outline"
            size="sm"
            className="gap-2 rounded-none border-0 border-r border-input px-4 hover:bg-accent disabled:opacity-50"
            disabled={isPublishDisabled}
            onClick={handleOpenPublishDialog}
          >
            {isPending && <Loader2 className="h-4 w-4 animate-spin" />}
            {isPublishedAndClean && <span className="h-2.5 w-2.5 rounded-full bg-green-500" />}
            {isPublishedAndClean ? "Published" : "Publish"}
          </Button>
          <DropdownMenuTrigger asChild>
            <Button
              variant="outline"
              size="sm"
              aria-label="Open publish actions"
              className="rounded-none border-0 px-3 hover:bg-accent"
              disabled={isDropdownDisabled}
            >
              <ChevronDown className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
        </div>
        <DropdownMenuContent align="end">
          <DropdownMenuItem
            onClick={(e) => {
              if (hasUnsavedChanges || isPublishedAndClean) {
                e.preventDefault();
                return;
              }
              handleOpenPublishDialog();
            }}
            disabled={hasUnsavedChanges || isPublishedAndClean}
          >
            Publish
          </DropdownMenuItem>
          <DropdownMenuItem
            onClick={(e) => {
              if (!isPublished) {
                e.preventDefault();
                return;
              }
              setIsUnpublishDialogOpen(true);
            }}
            disabled={!isPublished}
          >
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
