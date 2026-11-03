"use client";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Input } from "@/components/ui-kits/input/input";
import { isErrorWithErrors } from "@/lib/error";
import { useUpdateWorkflow } from "@blocks-workflow/hooks/use-workflow-api";
import { z } from "zod";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const renameWorkflowSchema = z.object({
  name: z.string().trim().min(1, { error: () => tStatic("logic.workflow", "SCHEMAS.WORKFLOW_NAME_IS_REQUIRED") }),
});

type RenameWorkflowFormValues = z.infer<typeof renameWorkflowSchema>;

type RenameWorkflowProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workflowId: string;
  initialName: string;
};

export const RenameWorkflow = ({
  open,
  onOpenChange,
  workflowId,
  initialName,
}: RenameWorkflowProps) => {
  const { t } = useT("logic.workflow");
  const { isPending, mutateAsync } = useUpdateWorkflow();

  const form = useForm<RenameWorkflowFormValues>({
    defaultValues: { name: initialName },
    resolver: zodResolver(renameWorkflowSchema),
  });

  const handleSubmit = async (values: RenameWorkflowFormValues) => {
    try {
      const payload = { itemId: workflowId, name: values.name };

      const res = await mutateAsync(payload);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: t("RENAME_WORKFLOW.WORKFLOW_SUCCESSFULLY_RENAMED") });
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error))
        return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("RENAME_WORKFLOW.FAILED_TO_RENAME_WORKFLOW") });
    }
  };

  const handleModalClose = (isOpen: boolean) => {
    onOpenChange(isOpen);
  };

  return (
    <Dialog open={open} onOpenChange={handleModalClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{t("RENAME_WORKFLOW.RENAME_WORKFLOW")}</DialogTitle>
          <DialogDescription>
            {t("RENAME_WORKFLOW.ENTER_A_NEW_NAME")}
          </DialogDescription>
        </DialogHeader>

        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(handleSubmit)}
            className="space-y-6"
          >
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("RENAME_WORKFLOW.WORKFLOW_NAME")}</FormLabel>
                  <FormControl>
                    <Input placeholder={t("RENAME_WORKFLOW.ENTER_WORKFLOW_NAME")} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <div className="flex justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => handleModalClose(false)}
                disabled={isPending}
              >
                {t("common:CANCEL")}
              </Button>
              <Button type="submit" disabled={isPending}>
                {t("RENAME_WORKFLOW.RENAME")}
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
