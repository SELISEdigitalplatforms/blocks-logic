"use client";

import { useEffect, useState } from "react";
import { useFieldArray, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { format } from "date-fns";
import Editor, { type OnMount } from "@monaco-editor/react";
import { CalendarClock, Plus, Trash2, X, Loader2, Save } from "lucide-react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Button } from "@/components/ui-kits/button/button";
import { Calendar } from "@/components/ui-kits/calendar/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { Input } from "@/components/ui-kits/input/input";
import { Switch } from "@/components/ui-kits/switch/switch";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { cn, parseDateString } from "@/lib/utils";
import { isErrorWithErrors } from "@/lib/error";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useCreateSchedule, useUpdateSchedule } from "../../hooks/use-schedule-api";
import { ISchedule } from "../../types/schedule.service.type";
import {
  CRON_PRESETS,
  scheduleFormDefaultValues,
  scheduleFormSchema,
  ScheduleFormValues,
  toIsoOrNull,
  WEBHOOK_METHODS,
  webhookHeadersToArray,
  webhookHeadersToRecord,
} from "../schedule-form-dialog/utils";

export type ScheduleFormProps = {
  mode: "create" | "edit";
  schedule?: ISchedule | null;
  isLoadingSchedule?: boolean;
  onSuccess?: (scheduleId?: string) => void;
  onCancel?: () => void;
};

type DatePopoverFieldProps = {
  value?: string;
  // eslint-disable-next-line no-unused-vars
  onChange: (value: string) => void;
  placeholder?: string;
};

const DatePopoverField = ({ value, onChange, placeholder }: DatePopoverFieldProps) => {
  const [open, setOpen] = useState(false);
  const selected = value ? parseDateString(value) : undefined;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          className={cn(
            "h-10 w-full justify-start px-3 font-normal",
            !value && "text-muted-foreground",
          )}
        >
          <CalendarClock className="mr-2 h-4 w-4 shrink-0 opacity-70" />
          <span className="truncate">
            {selected ? format(selected, "MMM d, yyyy") : placeholder}
          </span>
          {value && (
            <span
              role="button"
              tabIndex={0}
              className="ml-auto rounded-sm opacity-60 transition-opacity hover:opacity-100 focus:outline-none"
              onClick={(event) => {
                event.stopPropagation();
                onChange("");
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  event.stopPropagation();
                  onChange("");
                }
              }}
            >
              <X className="h-4 w-4" />
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="single"
          selected={selected}
          defaultMonth={selected}
          onSelect={(date) => {
            onChange(date ? format(date, "yyyy-MM-dd") : "");
            setOpen(false);
          }}
        />
      </PopoverContent>
    </Popover>
  );
};

export const ScheduleForm = ({
  mode,
  schedule,
  isLoadingSchedule,
  onSuccess,
  onCancel,
}: ScheduleFormProps) => {
  const isEdit = mode === "edit";
  const { resolvedTheme } = useTheme();
  const monacoTheme = resolvedTheme === "dark" ? "vs-dark" : "vs";
  const createMutation = useCreateSchedule();
  const updateMutation = useUpdateSchedule();
  const isPending = createMutation.isPending || updateMutation.isPending;

  const form = useForm<ScheduleFormValues>({
    defaultValues: scheduleFormDefaultValues,
    resolver: zodResolver(scheduleFormSchema),
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: "webhook.headers",
  });

  useEffect(() => {
    if (isEdit && schedule) {
      form.reset({
        name: schedule.name ?? "",
        description: schedule.description ?? "",
        webhook: {
          url: schedule.webhook?.url ?? "",
          method: schedule.webhook?.method ?? "POST",
          headers: webhookHeadersToArray(schedule.webhook?.headers),
          signingSecret: schedule.webhook?.signingSecret ?? "",
        },
        cronExpression: schedule.cronExpression,
        payload: schedule.payload,
        startDate: schedule.startDate
          ? format(parseDateString(schedule.startDate), "yyyy-MM-dd")
          : "",
        endDate: schedule.endDate ? format(parseDateString(schedule.endDate), "yyyy-MM-dd") : "",
        isActive: schedule.isActive,
      });
    } else if (!isEdit) {
      form.reset(scheduleFormDefaultValues);
    }
  }, [isEdit, schedule, form]);

  const handleEditorMount: OnMount = (editor) => {
    const container = editor.getContainerDomNode();
    const stopKeyPropagation = (event: KeyboardEvent) => event.stopPropagation();
    const textarea = container.querySelector<HTMLTextAreaElement>("textarea.inputarea");
    if (textarea) textarea.tabIndex = -1;
    container.addEventListener("keydown", stopKeyPropagation);
    container.addEventListener("keyup", stopKeyPropagation);
    container.addEventListener("keypress", stopKeyPropagation);
    editor.onDidDispose(() => {
      container.removeEventListener("keydown", stopKeyPropagation);
      container.removeEventListener("keyup", stopKeyPropagation);
      container.removeEventListener("keypress", stopKeyPropagation);
    });
  };

  const handleSubmit = async (values: ScheduleFormValues) => {
    const basePayload = {
      name: values.name,
      description: values.description || null,
      payload: values.payload,
      cronExpression: values.cronExpression,
      startDate: toIsoOrNull(values.startDate),
      endDate: toIsoOrNull(values.endDate),
      webhook: {
        url: values.webhook.url,
        method: values.webhook.method,
        headers: webhookHeadersToRecord(values.webhook.headers),
        signingSecret: values.webhook.signingSecret || null,
      },
    };

    try {
      if (isEdit && schedule) {
        const res = await updateMutation.mutateAsync({
          itemId: schedule.itemId,
          ...basePayload,
          isActive: values.isActive,
        });

        if (!res.isSuccess) return showErrorToast({ errors: res.errors });
        showSuccessToast({
          description: "Schedule updated successfully.",
        });
        onSuccess?.(schedule.itemId);
      } else {
        const res = await createMutation.mutateAsync(basePayload);
        if (!res.isSuccess) return showErrorToast({ errors: res.errors });
        showSuccessToast({
          description: "Schedule successfully created.",
        });
        onSuccess?.(res.itemId ?? undefined);
      }
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to save schedule" });
    }
  };

  if (isLoadingSchedule) {
    return (
      <div className="flex min-h-[400px] items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(handleSubmit)} className="space-y-6">
        {/* Header: Title, Description, Active Toggle, and Save Action Button */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between mt-4">
          <div>
            <h2 className="text-2xl font-bold tracking-tight text-foreground">
              {isEdit ? "Edit Schedule" : "Create Schedule"}
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Configure trigger timing, webhook destination, and payloads.
            </p>
          </div>

          <div className="flex items-center gap-4">
            <div className="flex items-center gap-2.5">
              <FormField
                control={form.control}
                name="isActive"
                render={({ field }) => (
                  <FormItem className="flex items-center space-y-0">
                    <FormControl>
                      <Switch size="md" checked={field.value} onCheckedChange={field.onChange} />
                    </FormControl>
                  </FormItem>
                )}
              />
              <span className="text-sm font-medium text-foreground">Active</span>
            </div>

            {onCancel && (
              <Button type="button" variant="outline" onClick={onCancel} disabled={isPending}>
                <X className="h-4 w-4 mr-2" />
                Cancel
              </Button>
            )}

            <Button type="submit" disabled={isPending} className="gap-2 w-fit">
              {isPending ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Saving…
                </>
              ) : isEdit ? (
                <>
                  <Save className="h-4 w-4" />
                  Save Changes
                </>
              ) : (
                <>
                  <Plus className="h-4 w-4" />
                  Create
                </>
              )}
            </Button>
          </div>
        </div>

        {/* Card Body */}
        <Card className="w-full border shadow-sm">
          <CardContent className="p-2">
            {/* 2-Column Form Body */}
            <div className="grid grid-cols-1 gap-8 lg:grid-cols-2">
              {/* LEFT COLUMN: GENERAL + TIMING & RECURRENCE */}
              <div className="space-y-8">
                {/* 1. GENERAL */}
                <div className="space-y-4">
                  <h3 className="text-xs font-bold tracking-wider text-muted-foreground uppercase">
                    General
                  </h3>

                  <FormField
                    control={form.control}
                    name="name"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Name</FormLabel>
                        <FormControl>
                          <Input placeholder="Schedule name" maxLength={100} {...field} />
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
                        <FormLabel>Description</FormLabel>
                        <FormControl>
                          <Textarea
                            placeholder="Brief summary of the scheduled task"
                            className="min-h-[85px] resize-none"
                            maxLength={500}
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>

                {/* 2. TIMING & RECURRENCE */}
                <div className="space-y-4">
                  <h3 className="text-xs font-bold tracking-wider text-muted-foreground uppercase">
                    Timing & Recurrence
                  </h3>

                  <FormField
                    control={form.control}
                    name="cronExpression"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Cron Expression</FormLabel>
                        <FormControl>
                          <Input placeholder="0 0 * * *" className="font-mono" {...field} />
                        </FormControl>
                        <div className="flex flex-wrap gap-1.5 pt-1">
                          {CRON_PRESETS.map((preset) => (
                            <button
                              key={preset.value}
                              type="button"
                              className={cn(
                                "rounded-md border px-2.5 py-1 text-xs transition-all",
                                field.value === preset.value
                                  ? "border-primary bg-primary/10 font-medium text-primary"
                                  : "border-input bg-background text-muted-foreground hover:border-muted-foreground/30 hover:shadow-sm",
                              )}
                              onClick={() => field.onChange(preset.value)}
                            >
                              {preset.label}
                            </button>
                          ))}
                        </div>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <FormField
                      control={form.control}
                      name="startDate"
                      render={({ field }) => (
                        <FormItem className="flex flex-col">
                          <FormLabel>Start Date</FormLabel>
                          <DatePopoverField
                            value={field.value}
                            onChange={field.onChange}
                            placeholder="mm/dd/yyyy"
                          />
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <FormField
                      control={form.control}
                      name="endDate"
                      render={({ field }) => (
                        <FormItem className="flex flex-col">
                          <FormLabel>End Date</FormLabel>
                          <DatePopoverField
                            value={field.value}
                            onChange={field.onChange}
                            placeholder="mm/dd/yyyy"
                          />
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </div>
                </div>
              </div>

              {/* RIGHT COLUMN: WEBHOOK CONFIGURATION + PAYLOAD */}
              <div className="space-y-8">
                {/* 3. WEBHOOK CONFIGURATION */}
                <div className="space-y-4">
                  <h3 className="text-xs font-bold tracking-wider text-muted-foreground uppercase">
                    Webhook Configuration
                  </h3>

                  {/* Endpoint: Method + URL */}
                  <div>
                    <FormLabel>Endpoint</FormLabel>
                    <div className="mt-1.5 flex flex-col gap-2 sm:flex-row sm:items-start">
                      <FormField
                        control={form.control}
                        name="webhook.method"
                        render={({ field }) => (
                          <FormItem className="w-full sm:w-28 shrink-0">
                            <Select value={field.value} onValueChange={field.onChange}>
                              <FormControl>
                                <SelectTrigger>
                                  <SelectValue placeholder="Method" />
                                </SelectTrigger>
                              </FormControl>
                              <SelectContent>
                                {WEBHOOK_METHODS.map((method) => (
                                  <SelectItem key={method} value={method}>
                                    {method}
                                  </SelectItem>
                                ))}
                              </SelectContent>
                            </Select>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                      <FormField
                        control={form.control}
                        name="webhook.url"
                        render={({ field }) => (
                          <FormItem className="flex-1">
                            <FormControl>
                              <Input
                                type="url"
                                placeholder="https://api.example.com/webhook"
                                {...field}
                              />
                            </FormControl>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                    </div>
                  </div>

                  {/* Signing Secret */}
                  <FormField
                    control={form.control}
                    name="webhook.signingSecret"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Signing Secret</FormLabel>
                        <FormControl>
                          <Input type="password" placeholder="Enter secret" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />

                  {/* Headers */}
                  <FormField
                    control={form.control}
                    name="webhook.headers"
                    render={() => (
                      <FormItem>
                        <FormLabel>Headers</FormLabel>
                        <div className="space-y-2">
                          {fields.map((headerField, index) => (
                            <div key={headerField.id} className="flex items-center gap-2">
                              <FormField
                                control={form.control}
                                name={`webhook.headers.${index}.key`}
                                render={({ field }) => (
                                  <FormControl>
                                    <Input
                                      placeholder="Header Key"
                                      className="font-mono text-xs"
                                      {...field}
                                    />
                                  </FormControl>
                                )}
                              />
                              <FormField
                                control={form.control}
                                name={`webhook.headers.${index}.value`}
                                render={({ field }) => (
                                  <FormControl>
                                    <Input
                                      placeholder="Header Value"
                                      className="font-mono text-xs"
                                      {...field}
                                    />
                                  </FormControl>
                                )}
                              />
                              <Button
                                type="button"
                                variant="ghost"
                                size="icon"
                                className="h-9 w-9 shrink-0 text-muted-foreground hover:text-destructive"
                                onClick={() => remove(index)}
                              >
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            </div>
                          ))}

                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="w-full border-dashed text-xs text-muted-foreground hover:text-foreground"
                            onClick={() => append({ key: "", value: "" })}
                          >
                            <Plus className="mr-1.5 h-3.5 w-3.5" />
                            Add Header
                          </Button>
                        </div>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>

                {/* 4. PAYLOAD */}
                <div className="space-y-3">
                  <h3 className="text-xs font-bold tracking-wider text-muted-foreground uppercase">
                    Payload
                  </h3>

                  <FormField
                    control={form.control}
                    name="payload"
                    render={({ field }) => (
                      <FormItem>
                        <FormControl>
                          <Editor
                            height="180px"
                            language="json"
                            theme={monacoTheme}
                            value={field.value}
                            onChange={(next) => field.onChange(next ?? "")}
                            onMount={handleEditorMount}
                            options={{
                              minimap: { enabled: false },
                              fontSize: 13,
                              scrollBeyondLastLine: false,
                              automaticLayout: true,
                              fixedOverflowWidgets: true,
                              tabSize: 2,
                            }}
                            className="overflow-hidden rounded-lg border"
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      </form>
    </Form>
  );
};
