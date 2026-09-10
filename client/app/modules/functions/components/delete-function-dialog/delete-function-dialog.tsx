import { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
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

/**
 * Deleting takes the endpoint, every version, run and log with it, so the design asks for the name
 * to be typed out rather than a plain confirm. The server still refuses while runs are active — that
 * answer is surfaced as-is.
 */
export const DeleteFunctionDialog = ({
  open,
  onOpenChange,
  fn,
  onDeleted,
}: DeleteFunctionDialogProps) => {
  const { mutateAsync, isPending } = useDeleteFunction();
  const [typedName, setTypedName] = useState("");

  const canDelete = !!fn && typedName.trim() === fn.name && !isPending;

  // Cleared on the way out rather than on the way in, so the field is always empty when it opens
  // without needing an effect to reset it.
  const handleOpenChange = (next: boolean) => {
    if (!next) setTypedName("");
    onOpenChange(next);
  };

  const confirmHandler = async () => {
    if (!fn || !canDelete) return;
    try {
      const res = await mutateAsync(fn.id);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Something went wrong" });
      showSuccessToast({ description: "Function deleted successfully" });
      handleOpenChange(false);
      onDeleted?.();
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Delete function</DialogTitle>
          <DialogDescription>
            This removes <span className="font-semibold text-foreground">{fn?.name}</span>, its
            endpoint, every version and all runs and logs. It cannot be undone, and it is refused
            while runs are still active.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="delete-function-name" className="text-xs font-semibold">
            Type <span className="font-mono">{fn?.name}</span> to confirm
          </Label>
          <Input
            id="delete-function-name"
            autoComplete="off"
            value={typedName}
            placeholder={fn?.name ?? ""}
            onChange={(e) => setTypedName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && canDelete) void confirmHandler();
            }}
          />
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => handleOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button variant="destructive" disabled={!canDelete} onClick={() => void confirmHandler()}>
            {isPending ? "Deleting…" : "Delete function"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
