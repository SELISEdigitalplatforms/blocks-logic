import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useDeleteProxy } from "../hooks";
import { Proxy } from "../types";
import { renderRichText } from "@/lib/rich-text";
import { useT } from "@seliseblocks/genesis-os";

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
  const { t } = useT("logic.proxy.components");
  const { mutateAsync, isPending } = useDeleteProxy();

  const confirmHandler = async () => {
    if (!proxy) return;
    try {
      const res = await mutateAsync(proxy.id);
      if (!res.isSuccess)
        return showErrorToast({
          errors: res.errors || t("DELETE_PROXY_DIALOG.FAILED_TO_DELETE_PROXY"),
        });
      showSuccessToast({ description: t("DELETE_PROXY_DIALOG.PROXY_DELETED_SUCCESSFULLY") });
      onOpenChange(false);
      onDeleted?.();
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("DELETE_PROXY_DIALOG.FAILED_TO_DELETE_PROXY") });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: t("DELETE_PROXY_DIALOG.DELETE_PROXY"),
          dialogSubtitle: (
            <>
              {renderRichText(
                t("DELETE_PROXY_DIALOG.ARE_YOU_SURE_YOU", { name: proxy?.name ?? "" }),
                [(children) => <span className="font-semibold">{children}</span>],
              )}
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
