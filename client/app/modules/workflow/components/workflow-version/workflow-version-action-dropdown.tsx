"use client";

import { useState } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { useParams } from "react-router-dom";
import { useRestoreWorkflow, useUpdateWorkflowVersion } from "../../hooks/use-workflow-api";
import { useWorkflowActions } from "../../hooks/use-workflow-actions";
import { WorkflowVersion } from "../../models/workflow.model";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";
import { PublishConfirmationModal, UnpublishConfirmationModal } from "../workflow-confirmation-modals";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

interface WorkflowVersionActionDropdownProps {
  version: WorkflowVersion;
  children: React.ReactNode;
}

export const WorkflowVersionActionDropdown = ({ version, children }: WorkflowVersionActionDropdownProps) => {
  const { id: workflowId } = useParams<{ id: string }>();
  const {
    handlePublishUnversioned: publishUnversioned,
    handleUnpublish: unpublish,
    isPublishingUnversioned,
    isUnpublishing,
  } = useWorkflowActions();
  const restoreWorkflow = useRestoreWorkflow();
  const updateWorkflowVersion = useUpdateWorkflowVersion();

  const [isPublishModalOpen, setIsPublishModalOpen] = useState(false);
  const [isUnpublishModalOpen, setIsUnpublishModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editVersionName, setEditVersionName] = useState("");
  const [editDescription, setEditDescription] = useState("");

  const handleOpenPublishModal = () => {
    setIsPublishModalOpen(true);
  };

  const handleOpenUnpublishModal = () => {
    setIsUnpublishModalOpen(true);
  };

  const handleOpenEditModal = () => {
    setEditVersionName(version.name || "");
    setEditDescription(version.description || "");
    setIsEditModalOpen(true);
  };

  const handlePublishSubmit = () => {
    if (!workflowId) return;
    publishUnversioned(workflowId, version.itemId, () => {
      setIsPublishModalOpen(false);
    });
  };

  const handleUnpublishSubmit = () => {
    if (!workflowId) return;
    unpublish(workflowId, () => {
      setIsUnpublishModalOpen(false);
    });
  };

  const handleEditSubmit = async () => {
    try {
      const response: any = await updateWorkflowVersion.mutateAsync({
        versionId: version.itemId,
        name: editVersionName || "Version Name",
        description: editDescription,
      });
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || "Failed to edit workflow version." });
      } else {
        setIsEditModalOpen(false);
        showSuccessToast({ description: "Workflow version details successfully updated." });
      }
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to edit workflow version." });
    }
  };

  const handleRestore = () => {
    restoreWorkflow.mutate({
      workflowId: workflowId || "",
      versionId: version.itemId,
    });
  };

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          {children}
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleOpenEditModal();
          }}>Edit version details</DropdownMenuItem>
          <DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleRestore();
          }}>Restore version</DropdownMenuItem>
          {!version.isPublished && (<DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleOpenPublishModal();
          }}>Publish version</DropdownMenuItem>)}
          {version.isPublished && (<DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleOpenUnpublishModal();
          }}>Unpublish version</DropdownMenuItem>)}
        </DropdownMenuContent>
      </DropdownMenu>

      <PublishConfirmationModal
        open={isPublishModalOpen}
        onOpenChange={setIsPublishModalOpen}
        onConfirm={handlePublishSubmit}
        isPending={isPublishingUnversioned}
        isVersion={true}
      />
      
      <UnpublishConfirmationModal
        open={isUnpublishModalOpen}
        onOpenChange={setIsUnpublishModalOpen}
        onConfirm={handleUnpublishSubmit}
        isPending={isUnpublishing}
        isVersion={true}
      />

      <PublishWorkflowModal
        open={isEditModalOpen}
        onOpenChange={setIsEditModalOpen}
        publishVersionName={editVersionName}
        setPublishVersionName={setEditVersionName}
        publishDescription={editDescription}
        setPublishDescription={setEditDescription}
        onPublish={handleEditSubmit}
        isPublishing={updateWorkflowVersion.isPending}
        mode="edit"
      />
    </>
  );
};
