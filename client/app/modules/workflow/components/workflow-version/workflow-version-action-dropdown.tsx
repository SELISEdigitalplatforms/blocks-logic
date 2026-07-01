"use client";

import { useState } from "react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { useParams } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { usePublishWorkflow, useUnpublishWorkflow, useRestoreWorkflow, useUpdateWorkflowVersion } from "../../hooks/use-workflow-api";
import { WorkflowVersion } from "../../models/workflow.model";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";
import { PublishConfirmationModal, UnpublishConfirmationModal } from "../workflow-confirmation-modals";

interface WorkflowVersionActionDropdownProps {
  version: WorkflowVersion;
  children: React.ReactNode;
}

export const WorkflowVersionActionDropdown = ({ version, children }: WorkflowVersionActionDropdownProps) => {
  const { id: workflowId } = useParams<{ id: string }>();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const publishWorkflow = usePublishWorkflow();
  const unpublishWorkflow = useUnpublishWorkflow();
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

  const handlePublishSubmit = async () => {
    try {
      await publishWorkflow.mutateAsync({
        workflowId: workflowId || "",
        projectKey,
        versionId: version.itemId,
      });
      setIsPublishModalOpen(false);
    } catch (error) {
      console.error(error);
    }
  };

  const handleUnpublishSubmit = async () => {
    try {
      await unpublishWorkflow.mutateAsync({
        workflowId: workflowId || "",
        projectKey,
      });
      setIsUnpublishModalOpen(false);
    } catch (error) {
      console.error(error);
    }
  };

  const handleEditSubmit = async () => {
    try {
      await updateWorkflowVersion.mutateAsync({
        projectKey,
        versionId: version.itemId,
        name: editVersionName || "Version Name",
        description: editDescription,
      });
      setIsEditModalOpen(false);
    } catch (error) {
      console.error(error);
    }
  };

  const handleRestore = () => {
    restoreWorkflow.mutate({
      workflowId: workflowId || "",
      projectKey,
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
            unpublishWorkflow.mutateAsync({
              workflowId: workflowId || "",
              projectKey,
            });
          }}>Unpublish version</DropdownMenuItem>)}
        </DropdownMenuContent>
      </DropdownMenu>

      <PublishConfirmationModal
        open={isPublishModalOpen}
        onOpenChange={setIsPublishModalOpen}
        onConfirm={handlePublishSubmit}
        isPending={publishWorkflow.isPending}
        isVersion={true}
      />
      
      <UnpublishConfirmationModal
        open={isUnpublishModalOpen}
        onOpenChange={setIsUnpublishModalOpen}
        onConfirm={handleUnpublishSubmit}
        isPending={unpublishWorkflow.isPending}
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
