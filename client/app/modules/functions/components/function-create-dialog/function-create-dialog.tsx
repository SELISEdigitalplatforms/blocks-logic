"use client";
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router";
import { useScopedPath, useT, tStatic } from "@seliseblocks/genesis-os";
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
import { renderRichText } from "@/lib/rich-text";

// Every template the picker shows, so a new one cannot be offered and then refused by the form (FN-12).
const TEMPLATE_VALUES = FUNCTION_TEMPLATES.map((t) => t.value) as [FunctionTemplate, ...FunctionTemplate[]];

// Limits from FEATURES-AND-UI §4.2. The server allows more, deliberately: this is the form's
// contract with the person typing, not the storage limit.
const createFunctionFormSchema = () =>
  z.object({
    name: z
      .string()
      .min(2, tStatic("logic.functions.components", "FUNCTION_CREATE_DIALOG.NAME_TOO_SHORT"))
      .max(64, tStatic("logic.functions.components", "FUNCTION_CREATE_DIALOG.NAME_TOO_LONG")),
    description: z
      .string()
      .max(200, tStatic("logic.functions.components", "FUNCTION_CREATE_DIALOG.DESCRIPTION_TOO_LONG"))
      .optional(),
    template: z.enum(TEMPLATE_VALUES),
  });

type CreateFunctionFormValues = z.infer<ReturnType<typeof createFunctionFormSchema>>;

type FunctionCreateDialogProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
};

export const FunctionCreateDialog = ({ open, onOpenChange }: FunctionCreateDialogProps) => {
  const { t: tr } = useT("logic.functions.components");
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { mutateAsync, isPending } = useCreateFunction();

  const form = useForm<CreateFunctionFormValues>({
    defaultValues: { name: "", description: "", template: "Minimal" },
    resolver: zodResolver(createFunctionFormSchema()),
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
      showSuccessToast({ description: tr("FUNCTION_CREATE_DIALOG.CREATED_DEPLOY_IT_TO") });
      onOpenChange(false);
      navigate(scoped(`functions/${created.id}`));
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: tr("FUNCTION_CREATE_DIALOG.FAILED_TO_CREATE_FUNCTION") });
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
            <DialogTitle className="tracking-tight">{tr("FUNCTION_CREATE_DIALOG.CREATE_FUNCTION")}</DialogTitle>
            <DialogDescription>
              {renderRichText(tr("FUNCTION_CREATE_DIALOG.PICK_A_STARTER"), [
                (text) => <code className="font-mono text-xs">{text}</code>,
              ])}
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
                  <FormLabel>{tr("common:NAME")}</FormLabel>
                  <FormControl>
                    <Input placeholder={tr("FUNCTION_CREATE_DIALOG.E_G_SEND_ORDER")} {...field} />
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
                  <FormLabel>{tr("FUNCTION_CREATE_DIALOG.TEMPLATE")}</FormLabel>
                  <div className="flex flex-col gap-2" role="radiogroup" aria-label={tr("FUNCTION_CREATE_DIALOG.TEMPLATE")}>
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
                    {renderRichText(tr("FUNCTION_CREATE_DIALOG.DESCRIPTION_0_OPTIONAL_0"), [(children) => <span className="ml-1.5 text-xs font-normal text-muted-foreground">{children}</span>])}
                  </FormLabel>
                  <FormControl>
                    <Textarea
                      placeholder={tr("FUNCTION_CREATE_DIALOG.WHAT_DOES_THIS_FUNCTION")}
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
                {tr("common:CANCEL")}
              </Button>
              <Button type="submit" disabled={isPending} className="min-w-28">
                {isPending ? tr("FUNCTION_CREATE_DIALOG.CREATING") : tr("common:CREATE")}
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
