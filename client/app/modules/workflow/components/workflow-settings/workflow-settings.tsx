"use client";

import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { Switch } from "@/components/ui-kits/switch/switch";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useUpdateWorkflowSettings } from "@blocks-workflow/hooks/use-workflow-api";
import { WorkflowAlertMode, WorkflowSettings as WorkflowSettingsModel } from "@blocks-workflow/models/workflow.model";
import { ErrorWorkflowPicker } from "./error-workflow-picker";

const settingsSchema = z
  .object({
    isEnabled: z.boolean(),
    workflowId: z.string(),
  })
  .superRefine((values, ctx) => {
    if (values.isEnabled && !values.workflowId)
      ctx.addIssue({ code: "custom", path: ["workflowId"], message: "Select a workflow" });
  });

type SettingsFormValues = z.infer<typeof settingsSchema>;

// Server error keys for the alert's fields.
const SERVER_FIELD_KEYS: Record<string, keyof SettingsFormValues> = {
  "Settings.Alert.WorkflowId": "workflowId",
};

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workflowId: string;
  settings: WorkflowSettingsModel | null | undefined;
};

/** Workflow settings: flat rows like n8n's settings modal. Saving never touches the editor's graph. */
export const WorkflowSettings = ({ open, onOpenChange, workflowId, settings }: Props) => {
  const { isPending, mutateAsync } = useUpdateWorkflowSettings();

  const form = useForm<SettingsFormValues>({
    defaultValues: {
      isEnabled: settings?.alert?.isEnabled ?? false,
      workflowId: settings?.alert?.workflowId ?? "",
    },
    resolver: zodResolver(settingsSchema),
  });
  const isEnabled = useWatch({ control: form.control, name: "isEnabled" });

  const handleSubmit = async (values: SettingsFormValues) => {
    try {
      const res = await mutateAsync({
        itemId: workflowId,
        settings: {
          // An alert that was never set and is off stays unset.
          alert:
            values.isEnabled || values.workflowId
              ? { isEnabled: values.isEnabled, mode: WorkflowAlertMode.Workflow, workflowId: values.workflowId || null }
              : null,
        },
      });
      if (!res.isSuccess) {
        const errors = (res.errors ?? {}) as Record<string, string>;
        let shown = false;
        for (const [key, message] of Object.entries(errors)) {
          const field = SERVER_FIELD_KEYS[key];
          if (field) {
            form.setError(field, { message });
            shown = true;
          }
        }
        if (!shown) showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: "Settings saved." });
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to save settings" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="w-[calc(100vw-32px)] max-w-[720px] gap-5 px-7 py-6">
        <DialogHeader>
          <DialogTitle className="text-xl">Workflow settings</DialogTitle>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={form.handleSubmit(handleSubmit)} className="flex flex-col gap-5">
            <div className="flex flex-col">
              <FormField
                control={form.control}
                name="isEnabled"
                render={({ field }) => (
                  <FormItem className="flex min-h-12 items-center justify-between space-y-0">
                    <FormLabel className="text-sm font-normal">Alert on failure</FormLabel>
                    <FormControl>
                      <Switch
                        aria-label="Alert on failure"
                        checked={field.value}
                        onCheckedChange={(checked) => {
                          field.onChange(checked);
                          if (!checked) form.clearErrors("workflowId");
                        }}
                        autoFocus
                      />
                    </FormControl>
                  </FormItem>
                )}
              />

              {/* Hidden while off; its value is kept and saved with the alert. */}
              {isEnabled && (
                <FormField
                  control={form.control}
                  name="workflowId"
                  render={({ field }) => (
                    <FormItem className="grid min-h-12 grid-cols-1 items-center gap-2 space-y-0 sm:grid-cols-[10fr_14fr]">
                      <FormLabel htmlFor="error-workflow" className="text-sm font-normal">
                        Error workflow
                      </FormLabel>
                      <div className="flex flex-col gap-1">
                        <ErrorWorkflowPicker
                          id="error-workflow"
                          currentWorkflowId={workflowId}
                          value={field.value}
                          onChange={(id) => {
                            field.onChange(id);
                            form.clearErrors("workflowId");
                          }}
                        />
                        <FormMessage />
                      </div>
                    </FormItem>
                  )}
                />
              )}
            </div>

            <div className="flex justify-end">
              <Button type="submit" className="h-10" disabled={isPending}>
                Save
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
