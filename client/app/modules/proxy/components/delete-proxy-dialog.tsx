import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useDeleteProxy } from "../hooks";
import { Proxy } from "../types";

type DeleteProxyDialogProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  proxy: Pick<Proxy, "id" | "name"> | null;
  onDeleted?: () => void;
};

export const DeleteProxyDialog = ({
  open,
  onOpenChange,
  proxy,
  onDeleted,
}: DeleteProxyDialogProps) => {
  const { mutateAsync, isPending } = useDeleteProxy();

  const confirmHandler = async () => {
    if (!proxy) return;
    try {
      const res = await mutateAsync(proxy.id);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to delete proxy" });
      showSuccessToast({ description: "Proxy deleted successfully." });
      onOpenChange(false);
      onDeleted?.();
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to delete proxy" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: "Delete Proxy",
          dialogSubtitle: (
            <>
              Are you sure you want to delete <span className="font-semibold">{proxy?.name}</span>?
              This action cannot be undone.
            </>
          ),
          confirmButton: "Delete",
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};
