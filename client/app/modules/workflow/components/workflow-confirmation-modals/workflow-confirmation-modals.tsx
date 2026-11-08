import { Dialog } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { useT } from "@seliseblocks/genesis-os";

interface ConfirmationModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: () => void;
  isPending: boolean;
  isVersion?: boolean;
}

export const PublishConfirmationModal = ({
  open,
  onOpenChange,
  onConfirm,
  isPending,
  isVersion,
}: ConfirmationModalProps) => {
  const { t } = useT("logic.workflow");
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: t(
            isVersion
              ? "WORKFLOW_CONFIRMATION_MODALS.PUBLISH_VERSION"
              : "WORKFLOW_CONFIRMATION_MODALS.PUBLISH_WORKFLOW",
          ),
          dialogSubtitle: t(
            isVersion
              ? "WORKFLOW_CONFIRMATION_MODALS.PUBLISH_VERSION_CONFIRM"
              : "WORKFLOW_CONFIRMATION_MODALS.PUBLISH_WORKFLOW_CONFIRM",
          ),
          confirmButton: t("WORKFLOW_CONFIRMATION_MODALS.PUBLISH"),
        }}
        onConfirm={onConfirm}
        onCancel={() => onOpenChange(false)}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};

export const UnpublishConfirmationModal = ({
  open,
  onOpenChange,
  onConfirm,
  isPending,
  isVersion,
}: ConfirmationModalProps) => {
  const { t } = useT("logic.workflow");
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: t(
            isVersion
              ? "WORKFLOW_CONFIRMATION_MODALS.UNPUBLISH_VERSION"
              : "WORKFLOW_CONFIRMATION_MODALS.UNPUBLISH_WORKFLOW",
          ),
          dialogSubtitle: t(
            isVersion
              ? "WORKFLOW_CONFIRMATION_MODALS.UNPUBLISH_VERSION_CONFIRM"
              : "WORKFLOW_CONFIRMATION_MODALS.UNPUBLISH_WORKFLOW_CONFIRM",
          ),
          confirmButton: t("WORKFLOW_CONFIRMATION_MODALS.UNPUBLISH"),
        }}
        onConfirm={onConfirm}
        onCancel={() => onOpenChange(false)}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};
