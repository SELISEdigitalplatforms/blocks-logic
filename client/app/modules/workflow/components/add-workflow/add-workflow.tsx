"use client";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
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
import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { Plus } from "lucide-react";
import {
  addWorkflowDefaultValues,
  AddWorkflowFormValues,
  addWorkflowSchema,
} from "./utils";
import { isErrorWithErrors } from "@/lib/error";
import { useCreateWorkflow } from "@blocks-workflow/hooks/use-workflow-api";
import { useNavigate } from "react-router-dom";

export const AddWorkflow = () => {
  const [open, onOpenChange] = useState(false);
  const { isPending, mutateAsync } = useCreateWorkflow();
  const navigate = useNavigate();

  const form = useForm<AddWorkflowFormValues>({
    defaultValues: addWorkflowDefaultValues,
    resolver: zodResolver(addWorkflowSchema),
  });

  const handleSubmit = async (values: AddWorkflowFormValues) => {
    try {
      const payload = {name: values.name};

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
      <DialogTrigger asChild>
        <Button
          size="sm"
          variant="ghost"
          className="text-primary hover:text-primary"
        >
          <Plus className="h-4 w-4" />
          <span className="sr-only sm:not-sr-only sm:ml-2.5">Add Workflow</span>
        </Button>
      </DialogTrigger>

      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Create workflow</DialogTitle>
          <DialogDescription>
            Set up a new workflow to automate your tasks and processes.
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
                Create
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
