"use client";
import { useEffect, useState } from "react";
import { useFieldArray, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { format } from "date-fns";
import Editor, { type OnMount } from "@monaco-editor/react";
import { Webhook, Braces, CalendarClock, Plus, Settings, Trash2, X } from "lucide-react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
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
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
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
} from "./utils";

type ScheduleFormDialogProps = {
  open: boolean;
  onOpenChange: (value: boolean) => void;
  schedule?: ISchedule;
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

export const ScheduleFormDialog = ({ open, onOpenChange, schedule }: ScheduleFormDialogProps) => {
  const isEdit = !!schedule;
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

  const [activeTab, setActiveTab] = useState<string>("general");

  const tabMeta = [
    { value: "general", label: "General", icon: Settings },
    { value: "webhook", label: "Webhook", icon: Webhook },
    { value: "schedule", label: "Timing", icon: CalendarClock },
    { value: "payload", label: "Payload", icon: Braces },
  ] as const;

  useEffect(() => {
    if (!open) return;
    form.reset(
      schedule
        ? {
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
            endDate: schedule.endDate
              ? format(parseDateString(schedule.endDate), "yyyy-MM-dd")
              : "",
            isActive: schedule.isActive,
          }
        : scheduleFormDefaultValues,
    );
  }, [open, schedule, form]);

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
      const res = isEdit
        ? await updateMutation.mutateAsync({
            itemId: schedule.itemId,
            ...basePayload,
            isActive: values.isActive,
          })
        : await createMutation.mutateAsync(basePayload);

      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({
        description: isEdit ? "Schedule updated successfully." : "Schedule successfully created.",
      });
      onOpenChange(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to save schedule" });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] max-w-3xl flex-col gap-0 overflow-hidden p-0 sm:rounded-xl">
        <DialogHeader className="flex-row items-center gap-4 border-b bg-muted/40 px-6 py-5">
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <CalendarClock className="h-5 w-5" />
          </span>
          <div className="min-w-0 flex-1 space-y-0.5">
            <DialogTitle className="tracking-tight">
              {isEdit ? "Edit schedule" : "Create schedule"}
            </DialogTitle>
            <DialogDescription>
              {isEdit
                ? "Update the schedule configuration and save changes."
                : "Set up a webhook that runs on a recurring schedule."}
            </DialogDescription>
          </div>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={form.handleSubmit(handleSubmit)} className="flex min-h-0 flex-1 flex-col">
            <Tabs
              value={activeTab}
              onValueChange={setActiveTab}
              className="flex min-h-0 flex-1 flex-col"
            >
              <div className="border-b px-6">
                <TabsList className="h-auto w-full justify-start gap-1 bg-transparent p-0">
                  {tabMeta.map((tab) => {
                    const Icon = tab.icon;
                    return (
                      <TabsTrigger
                        key={tab.value}
                        value={tab.value}
                        className={cn(
                          "flex items-center gap-1.5 rounded-none border-b-2 border-transparent px-3 py-2.5 text-sm font-medium transition-colors",
                          "text-muted-foreground hover:text-foreground",
                          "data-[state=active]:border-primary data-[state=active]:text-foreground",
                        )}
                      >
                        <Icon className="h-4 w-4" />
                        {tab.label}
                      </TabsTrigger>
                    );
                  })}
                </TabsList>
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto px-6 py-6">
                <TabsContent value="general" className="space-y-5">
                  <FormField
                    control={form.control}
                    name="name"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Name</FormLabel>
                        <FormControl>
                          <Input placeholder="e.g. Daily order digest" {...field} />
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
                          <span className="ml-1.5 text-xs font-normal text-muted-foreground">
                            optional
                          </span>
                        </FormLabel>
                        <FormControl>
                          <Textarea
                            placeholder="What does this schedule do?"
                            className="min-h-[110px] resize-none"
                            {...field}
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  {isEdit && (
                    <div className="flex items-center justify-between rounded-xl border bg-muted/30 px-4 py-3">
                      <div className="space-y-0.5">
                        <p className="text-sm font-medium">Active</p>
                        <p className="text-xs text-muted-foreground">
                          Inactive schedules are skipped and unregistered.
                        </p>
                      </div>
                      <FormField
                        control={form.control}
                        name="isActive"
                        render={({ field }) => (
                          <FormItem>
                            <FormControl>
                              <Switch
                                size="md"
                                checked={field.value}
                                onCheckedChange={field.onChange}
                              />
                            </FormControl>
                          </FormItem>
                        )}
                      />
                    </div>
                  )}
                </TabsContent>
                <TabsContent value="webhook" className="space-y-5">
                  <div className="space-y-5">
                    <div className="flex items-start gap-3">
                      <FormField
                        control={form.control}
                        name="webhook.method"
                        render={({ field }) => (
                          <FormItem className="w-32 shrink-0">
                            <FormLabel>
                              <span className="hidden xl:inline">HTTP </span>Method
                            </FormLabel>
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
                            <FormLabel>Webhook URL</FormLabel>
                            <FormControl>
                              <Input
                                type="url"
                                placeholder="https://example.com/webhook"
                                {...field}
                              />
                            </FormControl>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                    </div>
                    <FormField
                      control={form.control}
                      name="webhook.signingSecret"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>
                            Signing Secret
                            <span className="ml-1.5 text-xs font-normal text-muted-foreground">
                              optional
                            </span>
                          </FormLabel>
                          <FormControl>
                            <Input type="password" placeholder="Secret key" {...field} />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <FormField
                      control={form.control}
                      name="webhook.headers"
                      render={() => (
                        <FormItem>
                          <div className="flex items-center justify-between">
                            <FormLabel>Headers</FormLabel>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              className="h-8 gap-1 px-2 text-primary"
                              onClick={() => append({ key: "", value: "" })}
                            >
                              <Plus className="h-3.5 w-3.5" />
                              Add Header
                            </Button>
                          </div>
                          <div className="space-y-2">
                            {fields.length === 0 && (
                              <p className="rounded-lg border border-dashed bg-background px-3 py-4 text-center text-sm text-muted-foreground">
                                No custom headers yet.
                              </p>
                            )}
                            {fields.map((headerField, index) => (
                              <div key={headerField.id} className="flex items-center gap-2">
                                <FormField
                                  control={form.control}
                                  name={`webhook.headers.${index}.key`}
                                  render={({ field }) => (
                                    <FormControl>
                                      <Input
                                        placeholder="Key"
                                        className="rounded-r-none border-dashed bg-background font-mono text-xs focus-visible:ring-0 focus-visible:ring-offset-0"
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
                                        placeholder="Value"
                                        className="rounded-l-none border-dashed bg-background font-mono text-xs focus-visible:ring-0 focus-visible:ring-offset-0"
                                        {...field}
                                      />
                                    </FormControl>
                                  )}
                                />
                                <Button
                                  type="button"
                                  variant="ghost"
                                  size="icon"
                                  className="h-8 w-8 shrink-0 text-muted-foreground hover:text-error"
                                  onClick={() => remove(index)}
                                >
                                  <Trash2 className="h-4 w-4" />
                                </Button>
                              </div>
                            ))}
                          </div>
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                    <p className="text-xs text-muted-foreground">
                      Deliveries are signed with{" "}
                      <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-[11px]">
                        x-signature-sha256
                      </code>{" "}
                      when a signing secret is set.
                    </p>
                  </div>
                </TabsContent>
                <TabsContent value="schedule" className="space-y-5">
                  <FormField
                    control={form.control}
                    name="cronExpression"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Cron Expression</FormLabel>
                        <FormControl>
                          <Input
                            placeholder="e.g. 0 9 * * MON-FRI"
                            className="font-mono"
                            {...field}
                          />
                        </FormControl>
                        <div className="flex flex-wrap gap-1.5 pt-1.5">
                          {CRON_PRESETS.map((preset) => (
                            <button
                              key={preset.value}
                              type="button"
                              className={cn(
                                "rounded-full border px-3 py-1 text-xs transition-all",
                                field.value === preset.value
                                  ? "border-primary bg-primary/10 text-primary"
                                  : "border-input bg-background text-muted-foreground hover:border-muted-foreground/30 hover:shadow-sm",
                              )}
                              onClick={() => field.onChange(preset.value)}
                            >
                              {preset.label}
                            </button>
                          ))}
                        </div>
                        <p className="text-xs text-muted-foreground">
                          Standard 5-field cron expression (minute hour day month weekday),
                          evaluated in UTC.
                        </p>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <div className="grid grid-cols-2 gap-4">
                    <FormField
                      control={form.control}
                      name="startDate"
                      render={({ field }) => (
                        <FormItem className="flex flex-col">
                          <FormLabel>Start Date</FormLabel>
                          <DatePopoverField
                            value={field.value}
                            onChange={field.onChange}
                            placeholder="Pick a start date"
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
                            placeholder="Pick an end date"
                          />
                          <FormMessage />
                        </FormItem>
                      )}
                    />
                  </div>
                </TabsContent>
                <TabsContent value="payload" className="space-y-5">
                  <FormField
                    control={form.control}
                    name="payload"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>
                          Payload
                          <span className="ml-1.5 text-xs font-normal text-muted-foreground">
                            optional
                          </span>
                        </FormLabel>
                        <Editor
                          height="200px"
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

                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </TabsContent>
              </div>
            </Tabs>
            <div className="flex items-center justify-end gap-2 border-t bg-muted/40 px-6 py-4">
              <Button
                type="button"
                variant="outline"
                className="border-transparent bg-muted/60 hover:bg-muted"
                onClick={() => onOpenChange(false)}
                disabled={isPending}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={isPending} className="min-w-28">
                {isPending ? "Saving…" : isEdit ? "Save changes" : "Create schedule"}
              </Button>
            </div>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
