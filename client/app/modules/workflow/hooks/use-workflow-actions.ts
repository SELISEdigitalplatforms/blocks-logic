import { usePublishNewWorkflow, usePublishWorkflow, useUnpublishWorkflow } from "./use-workflow-api";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

type MutationResult = {
  isSuccess?: boolean;
  errors?: { Message?: string };
};

const toErrorMessage = (error: unknown): string =>
  error instanceof Error ? error.message : "";

export const useWorkflowActions = () => {
  const { mutateAsync: publishNewWorkflowMutation, isPending: isPublishingNew } = usePublishNewWorkflow();
  const { mutateAsync: publishWorkflowMutation, isPending: isPublishingUnversioned } = usePublishWorkflow();
  const { mutateAsync: unpublishWorkflowMutation, isPending: isUnpublishing } = useUnpublishWorkflow();

  const handlePublishNew = async (
    workflowId: string,
    name: string,
    description: string,
    onSuccess?: () => void
  ) => {
    try {
      const response = (await publishNewWorkflowMutation({
        workflowId,
        name,
        description,
      })) as unknown as MutationResult;
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || "Failed to publish workflow." });
      } else {
        onSuccess?.();
        showSuccessToast({ description: "Workflow successfully published." });
      }
    } catch (error) {
      showErrorToast({ errors: toErrorMessage(error) || "Failed to publish workflow." });
    }
  };

  const handlePublishUnversioned = async (
    workflowId: string,
    versionId?: string,
    onSuccess?: () => void
  ) => {
    try {
      const payload: { workflowId: string; versionId?: string } = { workflowId };
      if (versionId) payload.versionId = versionId;

      const response = (await publishWorkflowMutation(payload)) as unknown as MutationResult;
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || "Failed to publish workflow." });
      } else {
        onSuccess?.();
        showSuccessToast({ description: "Workflow successfully published." });
      }
    } catch (error) {
      showErrorToast({ errors: toErrorMessage(error) || "Failed to publish workflow." });
    }
  };

  const handleUnpublish = async (
    workflowId: string,
    onSuccess?: () => void
  ) => {
    try {
      const response = (await unpublishWorkflowMutation({ workflowId })) as unknown as MutationResult;
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || "Failed to unpublish workflow." });
      } else {
        onSuccess?.();
        showSuccessToast({ description: "Workflow successfully unpublished." });
      }
    } catch (error) {
      showErrorToast({ errors: toErrorMessage(error) || "Failed to unpublish workflow." });
    }
  };

  return {
    handlePublishNew,
    handlePublishUnversioned,
    handleUnpublish,
    isPublishingNew,
    isPublishingUnversioned,
    isUnpublishing,
  };
};
