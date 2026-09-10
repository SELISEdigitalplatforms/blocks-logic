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
import { useCreateFunction } from "../../hooks/use-functions";

const createFunctionFormSchema = z.object({
  name: z.string().min(1, "Name is required").max(200),
  description: z.string().max(1000).optional(),
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
    defaultValues: { name: "", description: "" },
    resolver: zodResolver(createFunctionFormSchema),
  });

  useEffect(() => {
    if (open) form.reset({ name: "", description: "" });
  }, [open, form]);

  const handleSubmit = async (values: CreateFunctionFormValues) => {
    try {
      const created = await mutateAsync({
        name: values.name,
        description: values.description || null,
      });
      showSuccessToast({ description: "Function created." });
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
              Name it, then write the code. Its invoke URL comes from the id it is created with.
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
              name="description"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>
                    Description
                    <span className="ml-1.5 text-xs font-normal text-muted-foreground">optional</span>
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
                {isPending ? "Creating…" : "Create function"}
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
