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
      showSuccessToast({
        description: `Active version is now v${version.number} — routing switched, no rebuild.`,
      });
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
          dialogTitle: `Roll back to v${version?.number}?`,
          dialogSubtitle:
            `This re-points the active version to v${version?.number} — nothing is rebuilt, and the ` +
            "image is the one that was already built and tested. Runs already in flight finish on " +
            "the version they started with, and your editor's working copy is left untouched.",
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};
