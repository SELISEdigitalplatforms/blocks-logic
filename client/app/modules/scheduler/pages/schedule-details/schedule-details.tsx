"use client";

import { useEffect, useState } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import Editor from "@monaco-editor/react";
import {
  CalendarClock,
  Check,
  Copy,
  Eye,
  EyeOff,
  Loader2,
  Pen,
  Trash2,
  Webhook,
  Braces,
} from "lucide-react";
import { useTheme } from "@seliseblocks/genesis-os/hooks";
import { useScopedPath, useT, tStatic } from "@seliseblocks/genesis-os";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
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
import { isErrorWithErrors } from "@/lib/error";
import { useGetScheduleById, useUpdateSchedule } from "../../hooks/use-schedule-api";
import { ScheduleKind } from "../../types/schedule.service.type";
import { CRON_PRESETS } from "../../components/schedule-form-dialog/utils";
import { DeleteScheduleDialog } from "../../components/delete-schedule";
import { ScheduleExecutionsTab } from "../../components/schedule-executions-tab/schedule-executions-tab";
import { ScheduleTimingFields } from "../../components/schedule-timing-fields";
import { UTC } from "../../utils/timezone";

export const ScheduleDetails = () => {
  const { t } = useT("logic.scheduler");
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { pathname } = useLocation();
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
      showErrorToast({ errors: tStatic("logic.scheduler", "SCHEDULE_DETAILS.SCHEDULE_NOT_FOUND") });
      navigate(scoped("/app/schedule"));
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
        description: checked ? t("SCHEDULE_DETAILS.SCHEDULE_ACTIVATED") : t("SCHEDULE_DETAILS.SCHEDULE_DEACTIVATED"),
      });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("SCHEDULE_DETAILS.FAILED_TO_UPDATE_SCHEDULE") });
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
  const timeZone = schedule.timeZone ?? UTC;
  const cronPreset = CRON_PRESETS.find((p) => p.value === schedule.cronExpression);
  const headersList = Object.entries(schedule.webhook?.headers ?? {});

  return (
    <div className="flex min-h-screen flex-col">
      {/* Breadcrumb Bar */}
      <div className="px-6 pt-4 pb-2">
        <PageBreadcrumb
          breadcrumbIndex={3}
          customTitles={{ [pathname]: schedule.name || null }}
        />
      </div>

      <div className="flex-1 px-6 mt-4 space-y-6">
        {/* Header Section */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="flex items-center gap-2.5">
              <h1 className="text-2xl font-bold tracking-tight">
                {schedule.name || t("SCHEDULE_DETAILS.UNTITLED_SCHEDULE")}
              </h1>
              <Badge
                variant={schedule.isActive ? "success" : "error"}
                className="rounded-md px-2.5 py-0.5 text-xs font-semibold"
              >
                {schedule.isActive ? t("SCHEDULE_DETAILS.ACTIVE") : t("SCHEDULE_DETAILS.INACTIVE")}
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
              <span className="text-xs font-medium text-muted-foreground">{t("common:STATUS")}</span>
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
              {t("common:EDIT")}
            </Button>

            <Button
              variant="outline"
              size="sm"
              className="gap-1.5 text-destructive hover:bg-destructive/10 hover:text-destructive"
              disabled={isInternal}
              onClick={() => setIsDeleteDialogOpen(true)}
            >
              <Trash2 className="h-3.5 w-3.5" />
              {t("common:DELETE")}
            </Button>
          </div>
        </div>

        {/* Tabs */}
        <Tabs value={activeTab} onValueChange={setActiveTab} className="flex flex-col gap-4">
          <TabsList className="w-fit">
            <TabsTrigger value="general" className="gap-2">
              {t("SCHEDULE_DETAILS.OVERVIEW")}
            </TabsTrigger>
            <TabsTrigger value="executions" className="gap-2">
              {t("SCHEDULE_DETAILS.EXECUTIONS")}
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
                      <CardTitle className="text-base">{t("SCHEDULE_DETAILS.TIMING_SCHEDULE")}</CardTitle>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div>
                      <span className="text-xs font-medium text-muted-foreground">{t("SCHEDULE_DETAILS.CRON_EXPRESSION")}</span>
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
                        {t("SCHEDULE_DETAILS.EVALUATED_IN", { timeZone })}</p>
                    </div>

                    <ScheduleTimingFields schedule={schedule} />
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
                        <CardTitle className="text-base">{t("SCHEDULE_DETAILS.PAYLOAD")}</CardTitle>
                        <CardDescription>
                          {t("SCHEDULE_DETAILS.BODY_SENT_TO_THE")}
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
                            {t("common:COPIED")}
                          </>
                        ) : (
                          <>
                            <Copy className="h-3.5 w-3.5" />
                            {t("SCHEDULE_DETAILS.COPY_PAYLOAD")}
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
                      <p className="text-sm text-muted-foreground">{t("SCHEDULE_DETAILS.NO_PAYLOAD_SPECIFIED")}</p>
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
                      <CardTitle className="text-base">{t("SCHEDULE_DETAILS.WEBHOOK_CONFIGURATION")}</CardTitle>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    <div>
                      <span className="text-xs font-medium text-muted-foreground">{t("SCHEDULE_DETAILS.ENDPOINT_URL")}</span>
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
                      <span className="text-xs font-medium text-muted-foreground">{t("SCHEDULE_DETAILS.SIGNING_SECRET")}</span>
                      <div className="mt-1 flex items-center justify-between rounded-lg border bg-muted/20 px-3 py-2">
                        <span className="font-mono text-xs text-foreground">
                          {schedule.webhook?.signingSecret
                            ? showSecret
                              ? schedule.webhook.signingSecret
                              : "••••••••••••••••••••••••••••••"
                            : t("SCHEDULE_DETAILS.NONE")}
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
                      <span className="text-xs font-medium text-muted-foreground">{t("SCHEDULE_DETAILS.CUSTOM_HEADERS")}</span>
                      {headersList.length === 0 ? (
                        <p className="mt-1 text-xs text-muted-foreground">{t("SCHEDULE_DETAILS.NO_CUSTOM_HEADERS_CONFIGURED")}</p>
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


          {/* Executions Tab */}
          <TabsContent value="executions">
            <ScheduleExecutionsTab schedule={schedule} active={activeTab === "executions"} />
          </TabsContent>
        </Tabs>
      </div>

      <DeleteScheduleDialog
        open={isDeleteDialogOpen}
        onOpenChange={setIsDeleteDialogOpen}
        onDeleted={() => navigate(scoped("schedule"))}
        schedule={schedule}
      />
    </div>
  );
};
