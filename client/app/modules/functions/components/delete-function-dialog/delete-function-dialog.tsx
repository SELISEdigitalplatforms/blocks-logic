import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useDeleteFunction } from "../../hooks/use-functions";
import { IFunctionSummary } from "../../types/function.types";

type DeleteFunctionDialogProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  fn: IFunctionSummary | null;
  onDeleted?: () => void;
};

export const DeleteFunctionDialog = ({
  open,
  onOpenChange,
  fn,
  onDeleted,
}: DeleteFunctionDialogProps) => {
  const { mutateAsync } = useDeleteFunction();

  const confirmHandler = async () => {
    if (!fn) return;
    try {
      const res = await mutateAsync(fn.id);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Something went wrong" });
      showSuccessToast({ description: "Function deleted successfully" });
      onOpenChange(false);
      onDeleted?.();
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: "Delete function",
          dialogSubtitle: `Are you sure you want to delete "${fn?.name ?? "this function"}"? This cannot be undone.`,
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
      />
    </Dialog>
  );
};
