import { usePublishNewWorkflow, usePublishWorkflow, useUnpublishWorkflow } from "./use-workflow-api";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useT } from "@seliseblocks/genesis-os";

type MutationResult = {
  isSuccess?: boolean;
  errors?: { Message?: string };
};

const toErrorMessage = (error: unknown): string =>
  error instanceof Error ? error.message : "";

export const useWorkflowActions = () => {
  const { t } = useT("logic.workflow");
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
        showErrorToast({ errors: response.errors?.Message || t("USE_WORKFLOW_ACTIONS.FAILED_TO_PUBLISH_WORKFLOW") });
      } else {
        onSuccess?.();
        showSuccessToast({ description: t("USE_WORKFLOW_ACTIONS.WORKFLOW_SUCCESSFULLY_PUBLISHED") });
      }
    } catch (error) {
      showErrorToast({ errors: toErrorMessage(error) || t("USE_WORKFLOW_ACTIONS.FAILED_TO_PUBLISH_WORKFLOW") });
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
        showErrorToast({ errors: response.errors?.Message || t("USE_WORKFLOW_ACTIONS.FAILED_TO_PUBLISH_WORKFLOW") });
      } else {
        onSuccess?.();
        showSuccessToast({ description: t("USE_WORKFLOW_ACTIONS.WORKFLOW_SUCCESSFULLY_PUBLISHED") });
      }
    } catch (error) {
      showErrorToast({ errors: toErrorMessage(error) || t("USE_WORKFLOW_ACTIONS.FAILED_TO_PUBLISH_WORKFLOW") });
    }
  };

  const handleUnpublish = async (
    workflowId: string,
    onSuccess?: () => void
  ) => {
    try {
      const response = (await unpublishWorkflowMutation({ workflowId })) as unknown as MutationResult;
      if (response && response.isSuccess === false) {
        showErrorToast({ errors: response.errors?.Message || t("USE_WORKFLOW_ACTIONS.FAILED_TO_UNPUBLISH_WORKFLOW") });
      } else {
        onSuccess?.();
        showSuccessToast({ description: t("USE_WORKFLOW_ACTIONS.WORKFLOW_SUCCESSFULLY_UNPUBLISHED") });
      }
    } catch (error) {
      showErrorToast({ errors: toErrorMessage(error) || t("USE_WORKFLOW_ACTIONS.FAILED_TO_UNPUBLISH_WORKFLOW") });
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
