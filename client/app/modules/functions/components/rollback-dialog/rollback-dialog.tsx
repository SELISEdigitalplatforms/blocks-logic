import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useRollbackFunction } from "../../hooks/use-functions";
import { IFunctionVersionSummary } from "../../types/version.types";

type RollbackDialogProps = {
  functionId: string;
  version: IFunctionVersionSummary | null;
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const RollbackDialog = ({ functionId, version, open, onOpenChange }: RollbackDialogProps) => {
  const { mutateAsync, isPending } = useRollbackFunction();

  const confirmHandler = async () => {
    if (!version) return;
    try {
      await mutateAsync({ functionId, versionNumber: version.number });
      showSuccessToast({ description: `Rolled back to v${version.number}.` });
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to roll back" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: "Roll back version",
          dialogSubtitle: `Make v${version?.number} the active version? In-flight runs are unaffected.`,
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};
