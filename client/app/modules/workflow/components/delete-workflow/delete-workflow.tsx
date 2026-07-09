import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useDeleteWorkflow } from "@blocks-workflow/hooks/use-workflow-api";

type DeleteWorkflowProps = {
  workflowId: string;
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const DeleteWorkflow = ({
  workflowId,
  open,
  onOpenChange,
}: DeleteWorkflowProps) => {
  const { mutateAsync } = useDeleteWorkflow();

  const confirmHandler = async () => {
    try {
      const res = await mutateAsync({
        id: workflowId,
      });
      if (!res.isSuccess)
        return showErrorToast({ errors: res.errors || "Something went wrong" });
      showSuccessToast({ description: "Workflow deleted successfully" });
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error))
        return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: "Delete Workflow",
          dialogSubtitle: `Are you sure you want to delete this workflow?`,
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
      />
    </Dialog>
  );
};
