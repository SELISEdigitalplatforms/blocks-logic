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
import { duplicateWorkflowSchema, DuplicateWorkflowFormValues } from "./schema";
import { isErrorWithErrors } from "@/lib/error";
import { useDuplicateWorkflow } from "@blocks-workflow/hooks/use-workflow-api";
import { addCopySuffix } from "@blocks-workflow/utils/add-copy-suffix.util";
import { useNavigate } from "react-router-dom";

type DuplicateWorkflowProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  workflowId: string;
  name: string;
};

export const DuplicateWorkflow = ({
  open,
  onOpenChange,
  workflowId,
  name,
}: DuplicateWorkflowProps) => {
  const { isPending, mutateAsync } = useDuplicateWorkflow();
  const navigate = useNavigate();

  const form = useForm<DuplicateWorkflowFormValues>({
    values: {
      name: name ? addCopySuffix(name) : "",
    } as DuplicateWorkflowFormValues,
    resolver: zodResolver(duplicateWorkflowSchema),
  });

  const handleSubmit = async (values: DuplicateWorkflowFormValues) => {
    try {
      const payload = {
        name: values.name,
        workflowId: workflowId,
      };

      const res = await mutateAsync(payload);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "Workflow successfully created." });
      form.reset();
      navigate(`/app/workflow/${res.itemId}`);
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error))
        return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to create workflow" });
    }
  };

  const handleModalClose = (isOpen: boolean) => {
    onOpenChange(isOpen);
    if (!isOpen) {
      form.reset();
    }
  };

  return (
    <Dialog open={open} onOpenChange={handleModalClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Duplicate workflow</DialogTitle>
          <DialogDescription>
            Duplicate this workflow to quickly build a similar automation.
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
                  <FormLabel>Workflow Name</FormLabel>
                  <FormControl>
                    <Input placeholder="Enter workflow name" {...field} />
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
                Cancel
              </Button>
              <Button type="submit" disabled={isPending}>
                Confirm
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
