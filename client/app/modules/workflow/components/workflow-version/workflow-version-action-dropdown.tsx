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
import { usePublishWorkflow, useRestoreWorkflow, useUpdateWorkflowVersion } from "../../hooks/use-workflow-api";
import { WorkflowVersion } from "../../models/workflow.model";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";

interface WorkflowVersionActionDropdownProps {
  version: WorkflowVersion;
  children: React.ReactNode;
}

export const WorkflowVersionActionDropdown = ({ version, children }: WorkflowVersionActionDropdownProps) => {
  const { id: workflowId } = useParams<{ id: string }>();
  const projectKey = useProjectStore().selectedProject?.tenantId || "";
  const publishWorkflow = usePublishWorkflow();
  const restoreWorkflow = useRestoreWorkflow();
  const updateWorkflowVersion = useUpdateWorkflowVersion();

  const [isPublishModalOpen, setIsPublishModalOpen] = useState(false);
  const [publishVersionName, setPublishVersionName] = useState("");
  const [publishDescription, setPublishDescription] = useState("");

  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editVersionName, setEditVersionName] = useState("");
  const [editDescription, setEditDescription] = useState("");

  const handleOpenPublishModal = () => {
    setPublishVersionName(version.name || "");
    setPublishDescription(version.description || "");
    setIsPublishModalOpen(true);
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
          <DropdownMenuItem onClick={(e) => {
            e.stopPropagation();
            handleOpenPublishModal();
          }}>Publish version</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <PublishWorkflowModal
        open={isPublishModalOpen}
        onOpenChange={setIsPublishModalOpen}
        publishVersionName={publishVersionName}
        setPublishVersionName={setPublishVersionName}
        publishDescription={publishDescription}
        setPublishDescription={setPublishDescription}
        onPublish={handlePublishSubmit}
        isPublishing={publishWorkflow.isPending}
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
