"use client";
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Zap } from "lucide-react";
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
import { Input } from "@/components/ui-kits/input/input";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { cn } from "@/lib/utils";
import { useCreateFunction } from "../../hooks/use-functions";
import { FUNCTION_TEMPLATES } from "../../constants/templates";
import { FunctionTemplate } from "../../types/function.types";

// Limits from FEATURES-AND-UI §4.2. The server allows more, deliberately: this is the form's
// contract with the person typing, not the storage limit.
const createFunctionFormSchema = z.object({
  name: z.string().min(2, "Give it at least 2 characters").max(64, "Keep it under 64 characters"),
  description: z.string().max(200, "Keep it under 200 characters").optional(),
  template: z.enum(["Minimal", "HttpEcho", "FetchTransform"]),
});

type CreateFunctionFormValues = z.infer<typeof createFunctionFormSchema>;

type FunctionCreateDialogProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const FunctionCreateDialog = ({ open, onOpenChange }: FunctionCreateDialogProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { mutateAsync, isPending } = useCreateFunction();

  const form = useForm<CreateFunctionFormValues>({
    defaultValues: { name: "", description: "", template: "Minimal" },
    resolver: zodResolver(createFunctionFormSchema),
  });

  useEffect(() => {
    if (open) form.reset({ name: "", description: "", template: "Minimal" });
  }, [open, form]);

  const handleSubmit = async (values: CreateFunctionFormValues) => {
    try {
      const created = await mutateAsync({
        name: values.name,
        description: values.description || null,
        template: values.template,
      });
      showSuccessToast({ description: "Created — deploy it to get a live endpoint." });
      onOpenChange(false);
      navigate(scoped(`functions/${created.id}`));
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to create function" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg sm:rounded-xl">
        <DialogHeader className="flex-row items-center gap-4">
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <Zap className="h-5 w-5" />
          </span>
          <div className="min-w-0 flex-1 space-y-0.5">
            <DialogTitle className="tracking-tight">Create function</DialogTitle>
            <DialogDescription>
              Pick a starter and name it. Its endpoint is{" "}
              <code className="font-mono text-xs">POST /api/fn/{"{id}"}</code>, so there is no slug
              to choose and nothing here has to be unique.
            </DialogDescription>
          </div>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={form.handleSubmit(handleSubmit)} className="space-y-5">
            <FormField
              control={form.control}
              name="name"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name</FormLabel>
                  <FormControl>
                    <Input placeholder="e.g. Send order confirmation" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="template"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Template</FormLabel>
                  <div className="flex flex-col gap-2" role="radiogroup" aria-label="Template">
                    {FUNCTION_TEMPLATES.map((template) => (
                      <button
                        key={template.value}
                        type="button"
                        role="radio"
                        aria-checked={field.value === template.value}
                        className={cn(
                          "flex items-start gap-2.5 rounded-lg border p-3 text-left transition-colors",
                          field.value === template.value
                            ? "border-primary bg-blocks-primary-25"
                            : "border-border hover:bg-surface-app",
                        )}
                        onClick={() => field.onChange(template.value as FunctionTemplate)}
                      >
                        <span
                          className={cn(
                            "mt-0.5 h-3.5 w-3.5 shrink-0 rounded-full border bg-background",
                            field.value === template.value
                              ? "border-[4px] border-primary"
                              : "border-border-medium-emphasis",
                          )}
                        />
                        <span className="flex min-w-0 flex-col gap-0.5">
                          <span className="text-xs font-semibold">{template.label}</span>
                          <span className="text-xs leading-relaxed text-medium-emphasis">
                            {template.description}
                          </span>
                        </span>
                      </button>
                    ))}
                  </div>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="description"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>
                    Description
                    <span className="ml-1.5 text-xs font-normal text-muted-foreground">
                      optional
                    </span>
                  </FormLabel>
                  <FormControl>
                    <Textarea
                      placeholder="What does this function do?"
                      className="min-h-[90px] resize-none"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <div className="flex items-center justify-end gap-2 pt-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => onOpenChange(false)}
                disabled={isPending}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={isPending} className="min-w-28">
                {isPending ? "Creating…" : "Create"}
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
