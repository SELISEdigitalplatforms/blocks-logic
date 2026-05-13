import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useProjectStore } from "@/store/useProjectStore";
import { useUpdateWorkflow } from "@blocks-workflow/hooks/use-workflow-api";

type ToggleStatusWorkflowProps = {
  isActive: boolean;
  workflowId: string;
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const ToggleStatusWorkflow = ({
  workflowId,
  open,
  onOpenChange,
  isActive,
}: ToggleStatusWorkflowProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { mutateAsync } = useUpdateWorkflow();

  const confirmHandler = async () => {
    try {
      if (!tenantId) return showErrorToast({ errors: "Something went wrong" });
      const res = await mutateAsync({
        itemId: workflowId,
        projectKey: tenantId,
        isActive: !isActive,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Something went wrong" });
      showSuccessToast({
        description: `Workflow ${isActive ? "deactivated" : "activated"} successfully`,
      });
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: `${isActive ? "Deactivate" : "Activate"} Workflow`,
          dialogSubtitle: isActive
            ? "This workflow will stop running until it is reactivated. Are you sure?"
            : "This workflow will start running and respond to its configured triggers. Are you sure?",
          confirmButton: "Confirm",
          cancelButton: "Cancel",
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
      />
    </Dialog>
  );
};
