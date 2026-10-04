"use client";

import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import Editor from "@monaco-editor/react";
import {
  CalendarClock,
  Check,
  Copy,
  Eye,
  EyeOff,
  History,
  Loader2,
  Pen,
  Trash2,
  Webhook,
  Braces,
} from "lucide-react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { useScopedPath } from "@seliseblocks/genesis-os";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui-kits/tabs/tabs";
import { Switch } from "@/components/ui-kits/switch/switch";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { formatDate, parseDateString } from "@/lib/utils";
import { isErrorWithErrors } from "@/lib/error";
import { useGetScheduleById, useUpdateSchedule } from "../../hooks/use-schedule-api";
import { ISchedule, ScheduleKind } from "../../types/schedule.service.type";
import { CRON_PRESETS } from "../../components/schedule-form-dialog/utils";
import { DeleteScheduleDialog } from "../../components/delete-schedule";

export const ScheduleDetails = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const params = useParams<{ scheduleId?: string; id?: string }>();
  const scheduleId = params.scheduleId || params.id;

  const { resolvedTheme } = useTheme();
  const monacoTheme = resolvedTheme === "dark" ? "vs-dark" : "vs";

  const [activeTab, setActiveTab] = useState<string>("general");
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [showSecret, setShowSecret] = useState(false);
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  const {
    data: schedule,
    isLoading,
    isFetched,
  } = useGetScheduleById({
    scheduleId,
  });

  const { mutateAsync: updateScheduleAsync, isPending: isUpdating } = useUpdateSchedule();

  useEffect(() => {
    if (scheduleId && isFetched && !isLoading && !schedule) {
      showErrorToast({ errors: "Schedule not found" });
      navigate(scoped("/app/schedule"));
    } else if (schedule?.name && scheduleId) {

      BREADCRUMB_CUSTOM_TITLES[`/schedule/${scheduleId}`] = schedule.name;
    }
  }, [scheduleId, isFetched, isLoading, schedule, navigate, scoped]);

  const handleCopy = (text: string, key: string) => {
    navigator.clipboard.writeText(text);
    setCopiedKey(key);
    setTimeout(() => setCopiedKey(null), 2000);
  };

  const handleToggleActive = async (checked: boolean) => {
    if (!schedule) return;
    try {
      const res = await updateScheduleAsync({
        itemId: schedule.itemId,
        name: schedule.name ?? "Schedule",
        description: schedule.description ?? null,
        payload: schedule.payload,
        cronExpression: schedule.cronExpression,
        startDate: schedule.startDate ?? null,
        endDate: schedule.endDate ?? null,
        isActive: checked,
        webhook: schedule.webhook,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({
        description: checked ? "Schedule activated." : "Schedule deactivated.",
      });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to update schedule status" });
    }
  };

  if (isLoading || !isFetched) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  if (!schedule) {
    return null;
  }

  const isInternal = schedule.kind === ScheduleKind.Internal;
  const cronPreset = CRON_PRESETS.find((p) => p.value === schedule.cronExpression);
  const headersList = Object.entries(schedule.webhook?.headers ?? {});

  return (
    <div className="flex min-h-screen flex-col">
      {/* Breadcrumb Bar */}
      <div className="px-6 pt-4 pb-2">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>

      <div className="flex-1 px-6 mt-4 space-y-6">
        {/* Header Section */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="flex items-center gap-2.5">
              <h1 className="text-2xl font-bold tracking-tight">
                {schedule.name || "Untitled Schedule"}
              </h1>
              <Badge
                variant={schedule.isActive ? "success" : "error"}
                className="rounded-md px-2.5 py-0.5 text-xs font-semibold"
              >
                {schedule.isActive ? "Active" : "Inactive"}
              </Badge>
              {/* {isInternal && (
                <Badge variant="info" className="rounded-md px-2 py-0.5 text-xs">
                  Workflow
                </Badge>
              )} */}
            </div>
            {schedule.description?.trim() ? (
              <p className="mt-1.5 text-sm text-muted-foreground">
                {schedule.description.trim()}
              </p>
            ) : null}
          </div>

          <div className="flex items-center gap-3">
            <div className="flex items-center gap-2 rounded-lg border bg-muted/30 px-3 py-1.5">
              <span className="text-xs font-medium text-muted-foreground">Status</span>
              <Switch
                size="md"
                checked={schedule.isActive}
                disabled={isUpdating || isInternal}
                onCheckedChange={handleToggleActive}
              />
            </div>

            <Button
              variant="outline"
              size="sm"
              className="gap-1.5"
              disabled={isInternal}
              onClick={() => navigate(scoped(`schedule/${schedule.itemId}/edit`))}
            >
              <Pen className="h-3.5 w-3.5" />
              Edit
            </Button>

            <Button
              variant="outline"
              size="sm"
              className="gap-1.5 text-destructive hover:bg-destructive/10 hover:text-destructive"
              disabled={isInternal}
              onClick={() => setIsDeleteDialogOpen(true)}
            >
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          </div>
        </div>

        {/* Tabs */}
        <Tabs value={activeTab} onValueChange={setActiveTab} className="flex flex-col gap-4">
          <TabsList className="w-fit">
            <TabsTrigger value="general" className="gap-2">
              Overview
            </TabsTrigger>
            <TabsTrigger value="executions" className="gap-2">
              Executions
            </TabsTrigger>
          </TabsList>

          {/* Overview Tab */}
          <TabsContent value="general" className="space-y-6">
            <div className="grid grid-cols-1 gap-6 lg:grid-cols-2 items-start">
              {/* Left Column: Timing & Schedule + Payload */}
              <div className="space-y-6">
                {/* 1. Timing & Recurrence */}
                <Card>
                  <CardHeader className="pb-3">
                    <div className="flex items-center gap-2">
                      <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-primary/10 text-primary">
                        <CalendarClock className="h-4 w-4" />
                      </span>
                      <CardTitle className="text-base">Timing & Schedule</CardTitle>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div>
                      <span className="text-xs font-medium text-muted-foreground">Cron Expression</span>
                      <div className="mt-1 flex items-center gap-2">
                        <code className="rounded bg-muted px-2.5 py-1 font-mono text-sm font-semibold text-foreground">
                          {schedule.cronExpression}
                        </code>
                        {cronPreset && (
                          <Badge variant="outline" className="text-xs">
                            {cronPreset.label}
                          </Badge>
                        )}
                      </div>
                      <p className="mt-1 text-xs text-muted-foreground">
                        Evaluated in UTC time.
                      </p>
                    </div>

                    <div className="grid grid-cols-2 gap-4 border-t pt-3">
                      <div>
                        <span className="text-xs font-medium text-muted-foreground">Start Date</span>
                        <p className="mt-0.5 text-sm font-medium">
                          {schedule.startDate
                            ? formatDate(parseDateString(schedule.startDate))
                            : "Immediate (No start date)"}
                        </p>
                      </div>
                      <div>
                        <span className="text-xs font-medium text-muted-foreground">End Date</span>
                        <p className="mt-0.5 text-sm font-medium">
                          {schedule.endDate
                            ? formatDate(parseDateString(schedule.endDate))
                            : "Indefinite (No end date)"}
                        </p>
                      </div>
                    </div>
                  </CardContent>
                </Card>

                {/* 2. Payload */}
                <Card>
                  <CardHeader className="pb-3 flex flex-row items-center justify-between">
                    <div className="flex items-center gap-2">
                      <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-primary/10 text-primary">
                        <Braces className="h-4 w-4" />
                      </span>
                      <div>
                        <CardTitle className="text-base">Payload</CardTitle>
                        <CardDescription>
                          Body sent to the webhook endpoint upon invocation.
                        </CardDescription>
                      </div>
                    </div>
                    {schedule.payload && (
                      <Button
                        variant="outline"
                        size="sm"
                        className="gap-1.5 text-xs"
                        onClick={() => handleCopy(schedule.payload, "payload")}
                      >
                        {copiedKey === "payload" ? (
                          <>
                            <Check className="h-3.5 w-3.5 text-green-500" />
                            Copied
                          </>
                        ) : (
                          <>
                            <Copy className="h-3.5 w-3.5" />
                            Copy Payload
                          </>
                        )}
                      </Button>
                    )}
                  </CardHeader>
                  <CardContent>
                    {schedule.payload ? (
                      <Editor
                        height="200px"
                        language="json"
                        theme={monacoTheme}
                        value={schedule.payload}
                        options={{
                          readOnly: true,
                          minimap: { enabled: false },
                          fontSize: 13,
                          scrollBeyondLastLine: false,
                          automaticLayout: true,
                          tabSize: 2,
                        }}
                        className="overflow-hidden rounded-lg border"
                      />
                    ) : (
                      <p className="text-sm text-muted-foreground">No payload specified.</p>
                    )}
                  </CardContent>
                </Card>
              </div>

              {/* Right Column: Webhook Configuration */}
              <div className="space-y-6">
                <Card>
                  <CardHeader className="pb-3">
                    <div className="flex items-center gap-2">
                      <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-primary/10 text-primary">
                        <Webhook className="h-4 w-4" />
                      </span>
                      <CardTitle className="text-base">Webhook Configuration</CardTitle>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div>
                      <span className="text-xs font-medium text-muted-foreground">Endpoint URL</span>
                      <div className="mt-1 flex items-center gap-2 rounded-lg border bg-muted/20 px-3 py-2">
                        <Badge variant="outline" className="font-mono text-xs uppercase">
                          {schedule.webhook?.method || "POST"}
                        </Badge>
                        <span className="min-w-0 flex-1 truncate font-mono text-xs">
                          {schedule.webhook?.url || "-"}
                        </span>
                        {schedule.webhook?.url && (
                          <Button
                            variant="ghost"
                            size="icon"
                            className="h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
                            onClick={() => handleCopy(schedule.webhook!.url, "webhookUrl")}
                          >
                            {copiedKey === "webhookUrl" ? (
                              <Check className="h-3.5 w-3.5 text-green-500" />
                            ) : (
                              <Copy className="h-3.5 w-3.5" />
                            )}
                          </Button>
                        )}
                      </div>
                    </div>

                    <div>
                      <span className="text-xs font-medium text-muted-foreground">Signing Secret</span>
                      <div className="mt-1 flex items-center justify-between rounded-lg border bg-muted/20 px-3 py-2">
                        <span className="font-mono text-xs text-foreground">
                          {schedule.webhook?.signingSecret
                            ? showSecret
                              ? schedule.webhook.signingSecret
                              : "••••••••••••••••••••••••••••••"
                            : "None"}
                        </span>
                        {schedule.webhook?.signingSecret && (
                          <div className="flex items-center gap-1">
                            <Button
                              variant="ghost"
                              size="icon"
                              className="h-7 w-7 text-muted-foreground hover:text-foreground"
                              onClick={() => setShowSecret((prev) => !prev)}
                            >
                              {showSecret ? (
                                <EyeOff className="h-3.5 w-3.5" />
                              ) : (
                                <Eye className="h-3.5 w-3.5" />
                              )}
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon"
                              className="h-7 w-7 text-muted-foreground hover:text-foreground"
                              onClick={() =>
                                handleCopy(schedule.webhook!.signingSecret!, "signingSecret")
                              }
                            >
                              {copiedKey === "signingSecret" ? (
                                <Check className="h-3.5 w-3.5 text-green-500" />
                              ) : (
                                <Copy className="h-3.5 w-3.5" />
                              )}
                            </Button>
                          </div>
                        )}
                      </div>
                    </div>

                    <div>
                      <span className="text-xs font-medium text-muted-foreground">Custom Headers</span>
                      {headersList.length === 0 ? (
                        <p className="mt-1 text-xs text-muted-foreground">No custom headers configured.</p>
                      ) : (
                        <div className="mt-1.5 space-y-1.5">
                          {headersList.map(([key, value]) => (
                            <div
                              key={key}
                              className="flex items-center justify-between rounded-md border bg-muted/20 px-3 py-1.5 font-mono text-xs"
                            >
                              <span className="font-semibold text-foreground">{key}</span>
                              <span className="text-muted-foreground">{value}</span>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </CardContent>
                </Card>
              </div>
            </div>
          </TabsContent>


          {/* Executions Tab (Coming Soon) */}
          <TabsContent value="executions">
            <Card className="border-dashed">
              <CardContent className="flex min-h-[360px] flex-col items-center justify-center px-6 py-12 text-center">
                <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                  <History className="h-8 w-8" />
                </div>
                <h3 className="mt-5 text-xl font-semibold tracking-tight text-foreground">
                  Execution History Coming Soon
                </h3>
                <p className="mt-2 max-w-md text-sm text-muted-foreground">
                  Detailed execution logs, delivery attempts, HTTP response statuses, and duration
                  metrics for this schedule will appear here in an upcoming release.
                </p>
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>
      </div>

      <DeleteScheduleDialog
        open={isDeleteDialogOpen}
        onOpenChange={(open) => {
          setIsDeleteDialogOpen(open);
          if (!open) {
            navigate(scoped("schedule"));
          }
        }}

        schedule={schedule}
      />
    </div>
  );
};
