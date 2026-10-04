import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useDeleteSchedule } from "../../hooks/use-schedule-api";
import { ISchedule } from "../../types/schedule.service.type";

type DeleteScheduleDialogProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  schedule: ISchedule | null;
};

export const DeleteScheduleDialog = ({
  open,
  onOpenChange,
  schedule,
}: DeleteScheduleDialogProps) => {
  const { mutateAsync } = useDeleteSchedule();

  const confirmHandler = async () => {
    if (!schedule) return;
    try {
      const res = await mutateAsync({ itemId: schedule.itemId });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Something went wrong" });
      showSuccessToast({ description: "Schedule deleted successfully" });
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
          dialogTitle: "Delete Schedule",
          dialogSubtitle: "Are you sure you want to delete this schedule?",
        }}
        onConfirm={confirmHandler}
        onCancel={() => onOpenChange(false)}
      />
    </Dialog>
  );
};
