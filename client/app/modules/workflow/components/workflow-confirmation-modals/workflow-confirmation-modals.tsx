import { Dialog } from "@/components/ui-kits/dialog/dialog";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";

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
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: `Publish ${isVersion ? "version" : "workflow"}`,
          dialogSubtitle: `Are you sure you want to publish this ${isVersion ? "version" : "workflow"}?`,
          confirmButton: "Publish",
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
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: `Unpublish ${isVersion ? "version" : "workflow"}`,
          dialogSubtitle: `Are you sure you want to unpublish this ${
            isVersion ? "version" : "workflow"
          }? It will no longer be available for execution.`,
          confirmButton: "Unpublish",
        }}
        onConfirm={onConfirm}
        onCancel={() => onOpenChange(false)}
        buttonState={{ confirm: { disable: isPending } }}
      />
    </Dialog>
  );
};
