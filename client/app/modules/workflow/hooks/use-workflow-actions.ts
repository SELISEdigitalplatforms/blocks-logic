import { usePublishNewWorkflow, usePublishWorkflow, useUnpublishWorkflow } from "./use-workflow-api";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";

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
      const response: any = await publishNewWorkflowMutation({
        workflowId,
        name,
        description,
      });
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || "Failed to publish workflow." });
      } else {
        onSuccess?.();
        showSuccessToast({ description: "Workflow successfully published." });
      }
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to publish workflow." });
    }
  };

  const handlePublishUnversioned = async (
    workflowId: string,
    versionId?: string,
    onSuccess?: () => void
  ) => {
    try {
      const payload: any = { workflowId };
      if (versionId) payload.versionId = versionId;

      const response: any = await publishWorkflowMutation(payload);
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || "Failed to publish workflow." });
      } else {
        onSuccess?.();
        showSuccessToast({ description: "Workflow successfully published." });
      }
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to publish workflow." });
    }
  };

  const handleUnpublish = async (
    workflowId: string,
    onSuccess?: () => void
  ) => {
    try {
      const response: any = await unpublishWorkflowMutation({ workflowId });
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || "Failed to unpublish workflow." });
      } else {
        onSuccess?.();
        showSuccessToast({ description: "Workflow successfully unpublished." });
      }
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to unpublish workflow." });
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
