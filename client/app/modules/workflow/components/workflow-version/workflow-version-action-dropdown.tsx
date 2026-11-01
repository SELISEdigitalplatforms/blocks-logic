"use client";

import { useState } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { useParams } from "react-router";
import { useRestoreWorkflow, useUpdateWorkflowVersion } from "../../hooks/use-workflow-api";
import { useWorkflowActions } from "../../hooks/use-workflow-actions";
import { WorkflowVersion } from "../../models/workflow.model";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";
import { PublishConfirmationModal, UnpublishConfirmationModal } from "../workflow-confirmation-modals";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useT } from "@seliseblocks/genesis-os";

interface WorkflowVersionActionDropdownProps {
  version: WorkflowVersion;
  children: React.ReactNode;
}

export const WorkflowVersionActionDropdown = ({ version, children }: WorkflowVersionActionDropdownProps) => {
  const { t } = useT("logic.workflow");
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
      const response = (await updateWorkflowVersion.mutateAsync({
        versionId: version.itemId,
        name: editVersionName || t("WORKFLOW_VERSION_ACTION_DROPDOWN.VERSION_NAME"),
        description: editDescription,
      })) as unknown as { isSuccess?: boolean; errors?: { Message?: string } };
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || t("WORKFLOW_VERSION_ACTION_DROPDOWN.FAILED_TO_EDIT_WORKFLOW") });
      } else {
        setIsEditModalOpen(false);
        showSuccessToast({ description: t("WORKFLOW_VERSION_ACTION_DROPDOWN.WORKFLOW_VERSION_DETAILS_SUCCESSFULLY") });
      }
    } catch (error) {
      showErrorToast({ errors: (error instanceof Error ? error.message : "") || t("WORKFLOW_VERSION_ACTION_DROPDOWN.FAILED_TO_EDIT_WORKFLOW") });
    }
  };

  const handleRestore = async () => {
    try {
      const response = (await restoreWorkflow.mutateAsync({
        workflowId: workflowId || "",
        versionId: version.itemId,
      })) as unknown as { isSuccess?: boolean; errors?: { Message?: string } };
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || t("WORKFLOW_VERSION_ACTION_DROPDOWN.FAILED_TO_RESTORE_WORKFLOW") });
      } else {
        showSuccessToast({ description: t("WORKFLOW_VERSION_ACTION_DROPDOWN.WORKFLOW_VERSION_SUCCESSFULLY_RESTORED") });
      }
    } catch (error) {
      showErrorToast({ errors: (error instanceof Error ? error.message : "") || t("WORKFLOW_VERSION_ACTION_DROPDOWN.FAILED_TO_RESTORE_WORKFLOW") });
    }
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
          }}>{t("WORKFLOW_VERSION_ACTION_DROPDOWN.EDIT_VERSION_DETAILS")}</DropdownMenuItem>
          <DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleRestore();
          }}>{t("WORKFLOW_VERSION_ACTION_DROPDOWN.RESTORE_VERSION")}</DropdownMenuItem>
          {!version.isPublished && (<DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleOpenPublishModal();
          }}>{t("WORKFLOW_VERSION_ACTION_DROPDOWN.PUBLISH_VERSION")}</DropdownMenuItem>)}
          {version.isPublished && (<DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleOpenUnpublishModal();
          }}>{t("WORKFLOW_VERSION_ACTION_DROPDOWN.UNPUBLISH_VERSION")}</DropdownMenuItem>)}
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
