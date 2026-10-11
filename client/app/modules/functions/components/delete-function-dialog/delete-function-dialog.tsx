import { useState } from "react";
import { TriangleAlert } from "lucide-react";
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
  /**
   * Only the id (to delete) and the name (to type back) are used. Asking for a whole
   * `IFunctionSummary` made the detail page invent run counts and dates it does not have, which
   * would quietly become wrong numbers the moment this dialog started showing any of them.
   */
  fn: Pick<IFunctionSummary, "id" | "name"> | null;
  onDeleted?: () => void;
};

/** Why the server refused the delete: what still holds the function (FN-66). */
type DeleteBlocked = { message: string; activeRuns: number; workflows: number };

/**
 * The refusal the server sends when runs have not finished or workflows still use the function
 * (`errors.deleteBlocked`). Null for any other error.
 */
export const readDeleteBlocked = (errors: Record<string, string | string[]> | undefined): DeleteBlocked | null => {
  if (!errors || String(errors.deleteBlocked) !== "true") return null;
  const count = (value: string | string[] | undefined) => Number(Array.isArray(value) ? value[0] : value) || 0;
  const message = errors.invalid_request;
  return {
    message: Array.isArray(message) ? message.join(" ") : (message ?? ""),
    activeRuns: count(errors.activeRuns),
    workflows: count(errors.workflows),
  };
};

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/**
 * Deleting takes the endpoint, every version, run and log with it, so the design asks for the name
 * to be typed out rather than a plain confirm. The server refuses while runs have not finished or a
 * workflow step uses the function; the dialog then says what deleting anyway would do and offers it
 * (force), still behind the typed name.
 */
export const DeleteFunctionDialog = ({
  open,
  onOpenChange,
  fn,
  onDeleted,
}: DeleteFunctionDialogProps) => {
  const { mutateAsync, isPending } = useDeleteFunction();
  const [typedName, setTypedName] = useState("");
  const [blocked, setBlocked] = useState<DeleteBlocked | null>(null);

  const canDelete = !!fn && typedName.trim() === fn.name && !isPending;

  // Cleared on the way out rather than on the way in, so the field is always empty when it opens
  // without needing an effect to reset it.
  const handleOpenChange = (next: boolean) => {
    if (!next) {
      setTypedName("");
      setBlocked(null);
    }
    onOpenChange(next);
  };

  const confirmHandler = async (force = false) => {
    if (!fn || !canDelete) return;
    try {
      const res = await mutateAsync({ functionId: fn.id, force });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Something went wrong" });
      showSuccessToast({ description: "Function deleted successfully" });
      handleOpenChange(false);
      onDeleted?.();
    } catch (error) {
      if (isErrorWithErrors(error)) {
        const refusal = readDeleteBlocked(error.errors);
        if (refusal) return setBlocked(refusal);
        return showErrorToast({ errors: error.errors });
      }
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
            endpoint, every version and all runs and logs. It cannot be undone. While runs have not
            finished or a workflow step uses this function, you are asked first.
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
              if (e.key === "Enter" && canDelete) void confirmHandler(!!blocked);
            }}
          />
        </div>

        {blocked && (
          <div
            role="alert"
            data-testid="delete-function-blocked"
            className="flex flex-col gap-2 rounded-md border border-warning-200 bg-warning-50 p-3 text-sm"
          >
            <div className="flex items-center gap-2 font-semibold text-warning-800">
              <TriangleAlert className="h-4 w-4 shrink-0" /> This function is still in use
            </div>
            <ul className="ml-6 list-disc text-foreground">
              {blocked.activeRuns > 0 && (
                <li>
                  {plural(blocked.activeRuns, "run has", "runs have")} not finished (queued or running).
                  Deleting now <span className="font-semibold">cancels</span> {blocked.activeRuns === 1 ? "it" : "them"}.
                </li>
              )}
              {blocked.workflows > 0 && (
                <li>
                  {plural(blocked.workflows, "workflow uses", "workflows use")} it. Those function steps
                  will fail at their next run.
                </li>
              )}
            </ul>
            {blocked.message && <p className="text-xs text-muted-foreground">{blocked.message}</p>}
            <p className="text-xs text-muted-foreground">
              Wait and try again, or delete it anyway.
            </p>
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => handleOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          {blocked ? (
            <Button
              variant="destructive"
              disabled={!canDelete}
              onClick={() => void confirmHandler(true)}
              data-testid="delete-function-anyway"
            >
              {isPending ? "Deleting…" : "Delete anyway"}
            </Button>
          ) : (
            <Button variant="destructive" disabled={!canDelete} onClick={() => void confirmHandler()}>
              {isPending ? "Deleting…" : "Delete function"}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
