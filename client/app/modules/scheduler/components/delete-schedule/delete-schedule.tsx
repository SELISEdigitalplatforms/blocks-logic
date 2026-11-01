import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useDeleteSchedule } from "../../hooks/use-schedule-api";
import { ISchedule } from "../../types/schedule.service.type";
import { useT } from "@seliseblocks/genesis-os";

type DeleteScheduleDialogProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  schedule: ISchedule | null;
  /** Runs only after a successful delete; cancelling just closes the dialog. */
  onDeleted?: () => void;
};

export const DeleteScheduleDialog = ({
  open,
  onOpenChange,
  schedule,
  onDeleted,
}: DeleteScheduleDialogProps) => {
  const { t } = useT("logic.scheduler");
  const { mutateAsync } = useDeleteSchedule();

  const confirmHandler = async () => {
    if (!schedule) return;
    try {
      const res = await mutateAsync({ itemId: schedule.itemId });
      if (!res.isSuccess)
        return showErrorToast({ errors: res.errors || t("DELETE_SCHEDULE.SOMETHING_WENT_WRONG") });
      showSuccessToast({ description: t("DELETE_SCHEDULE.SCHEDULE_DELETED_SUCCESSFULLY") });
      onOpenChange(false);
      onDeleted?.();
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("DELETE_SCHEDULE.SOMETHING_WENT_WRONG") });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <ConfirmationModal
        data={{
          dialogTitle: t("DELETE_SCHEDULE.DELETE_SCHEDULE"),
          dialogSubtitle: t("DELETE_SCHEDULE.ARE_YOU_SURE_YOU"),
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
      />
    </Dialog>
  );
};
