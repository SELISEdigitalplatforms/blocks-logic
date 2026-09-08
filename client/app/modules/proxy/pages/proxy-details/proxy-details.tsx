import { type ReactNode, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Eye, EyeOff, Loader2, Pause, Pen, Play } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { getProxyClientPath } from "../../constants";
import { useGetProxyById, useGetProxyOverview, useToggleProxy } from "../../hooks";
import { ProxyKeyValue } from "../../types";
import { ProxyMethodChips } from "../../components/proxy-method-chips";
import { ProxyStatusBadge } from "../../components/proxy-status-badge";
import { ProxyLogsTab } from "../../components/proxy-logs-tab";
import { ProxyHistoryTab } from "../../components/proxy-history-tab";

type ProxyValueSection = "headers" | "query" | "body";
type RevealedValues = Partial<Record<ProxyValueSection, Record<string, boolean>>>;

const bulletMask = "••••••••••••";

const pluralize = (count: number, singular: string, plural = `${singular}s`) =>
  `${count} ${count === 1 ? singular : plural}`;

const ValueRevealButton = ({
  shown,
  label,
  onClick,
}: {
  shown: boolean;
  label: string;
  onClick: () => void;
}) => (
  <Button
    type="button"
    variant="ghost"
    size="icon"
    className="h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
    aria-label={`${shown ? "Hide" : "Reveal"} ${label}`}
    title={`${shown ? "Hide" : "Reveal"} ${label}`}
    onClick={onClick}
  >
    {shown ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
  </Button>
);

const KeyValueRows = ({
  section,
  title,
  rows,
  empty,
  revealed,
  onToggle,
}: {
  section: ProxyValueSection;
  title: string;
  rows: ProxyKeyValue[];
  empty: string;
  revealed: RevealedValues;
  onToggle: (section: ProxyValueSection, id: string) => void;
}) => (
  <div>
    <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
      {title}
    </h3>
    <div className="mt-3 overflow-hidden rounded-lg border bg-card">
      <div className="border-b bg-muted/20 px-4 py-3 text-sm font-semibold text-muted-foreground">
        {title} · {rows.length}
      </div>
      {rows.length ? (
        <div className="divide-y">
          {rows.map((row, index) => {
            const id = `${row.key}-${index}`;
            const shown = Boolean(revealed[section]?.[id]);
            return (
              <div
                key={id}
                className="grid gap-3 px-4 py-3 text-sm sm:grid-cols-[minmax(160px,0.4fr)_minmax(0,1fr)_auto] sm:items-center"
              >
                <div className="flex min-w-0 items-center gap-2">
                  <span className="truncate font-mono font-semibold text-foreground">
                    {row.key}
                  </span>
                  {row.isSecretRef ? (
                    <Badge variant="success" className="w-fit shrink-0 lowercase">
                      vault
                    </Badge>
                  ) : null}
                </div>
                <span className="min-w-0 truncate font-mono text-muted-foreground">
                  {shown ? row.value : bulletMask}
                </span>
                <ValueRevealButton
                  shown={shown}
                  label={`${title.toLowerCase()} value ${row.key}`}
                  onClick={() => onToggle(section, id)}
                />
              </div>
            );
          })}
        </div>
      ) : (
        <p className="px-4 py-3 text-sm text-muted-foreground">{empty}</p>
      )}
    </div>
  </div>
);

const MetricCard = ({
  label,
  value,
  note,
  danger,
}: {
  label: string;
  value: string;
  note: string;
  danger?: boolean;
}) => (
  <Card className="rounded-xl">
    <CardContent className="p-0">
      <span className="text-xs font-semibold uppercase text-muted-foreground">{label}</span>
      <p className={cn("mt-2 text-3xl font-bold", danger ? "text-destructive" : "text-foreground")}>
        {value}
      </p>
      <p className="mt-1 text-sm text-muted-foreground">{note}</p>
    </CardContent>
  </Card>
);

const ConfigurationStepCard = ({
  eyebrow,
  children,
  active,
}: {
  eyebrow: string;
  children: ReactNode;
  active?: boolean;
}) => (
  <div
    className={cn(
      "min-h-[140px] rounded-lg border p-4",
      active ? "border-primary/40 bg-primary/10 text-primary" : "bg-card",
    )}
  >
    <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
      {eyebrow}
    </span>
    <div className="mt-3">{children}</div>
  </div>
);

const tabClass =
  "rounded-none border-b-2 border-transparent px-0 pb-2 pt-0 text-base data-[state=active]:border-primary data-[state=active]:bg-transparent data-[state=active]:text-primary data-[state=active]:shadow-none";

const ProxyOverviewSkeleton = () => (
  <div className="space-y-6">
    <div className="grid gap-4 md:grid-cols-3">
      {Array.from({ length: 3 }).map((_, index) => (
        <Card key={index} className="rounded-xl">
          <CardContent className="space-y-3 p-0">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-9 w-28" />
            <Skeleton className="h-4 w-20" />
          </CardContent>
        </Card>
      ))}
    </div>
    <Card className="rounded-xl">
      <CardHeader>
        <Skeleton className="h-6 w-36" />
      </CardHeader>
      <CardContent className="space-y-5">
        <Skeleton className="h-28 w-full" />
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-20 w-full" />
      </CardContent>
    </Card>
  </div>
);

const ProxyDetailsSkeleton = () => (
  <div className="flex min-h-screen flex-col" role="status" aria-label="Loading proxy details">
    <div className="px-6 pb-2 pt-4">
      <Skeleton className="h-5 w-64" />
    </div>
    <div className="flex-1 space-y-6 px-6 pb-8 pt-4">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="space-y-3">
          <div className="flex items-center gap-3">
            <Skeleton className="h-3.5 w-3.5 rounded-full" />
            <Skeleton className="h-8 w-56" />
            <Skeleton className="h-6 w-16 rounded-full" />
          </div>
          <Skeleton className="h-4 w-72" />
        </div>
        <div className="flex gap-3">
          <Skeleton className="h-10 w-24" />
          <Skeleton className="h-10 w-20" />
        </div>
      </div>

      <div className="space-y-4">
        <div className="flex gap-8 border-b border-border">
          <Skeleton className="h-8 w-20 rounded-none" />
          <Skeleton className="h-8 w-28 rounded-none" />
          <Skeleton className="h-8 w-32 rounded-none" />
        </div>
        <ProxyOverviewSkeleton />
      </div>
    </div>
  </div>
);

export const ProxyDetails = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const params = useParams<{ proxyId?: string }>();
  const proxyId = params.proxyId;
  const [showUpstream, setShowUpstream] = useState(false);
  const [revealedValues, setRevealedValues] = useState<RevealedValues>({});
  const [activeTab, setActiveTab] = useState("overview");
  const { data: proxy, isLoading, isFetched } = useGetProxyById(proxyId);
  const { data: overview, isLoading: isLoadingOverview } = useGetProxyOverview(proxy?.id, {
    enabled: Boolean(proxy),
  });
  const toggleProxy = useToggleProxy();
  const toggleLabel = proxy?.enabled ? "Pause" : "Resume";
  const pendingToggleLabel = proxy?.enabled ? "Pausing..." : "Resuming...";

  useEffect(() => {
    if (proxyId && isFetched && !isLoading && !proxy) {
      showErrorToast({ errors: "Proxy not found" });
      navigate(scoped("proxy"));
    } else if (proxy?.name && proxyId) {
      BREADCRUMB_CUSTOM_TITLES[`/proxy/${proxyId}`] = proxy.name;
    }
  }, [isFetched, isLoading, navigate, proxy, proxyId, scoped]);

  if (isLoading || !isFetched) {
    return <ProxyDetailsSkeleton />;
  }

  if (!proxy) return null;

  // The rolling-24h tiles come from the server (GetOverview); the browser never recomputes them.
  const calls24h = overview?.calls24h ?? proxy.calls24h;
  const averageLatency = overview?.avgLatencyMs ?? 0;
  const errorRate = (overview?.errorRatePct ?? 0).toFixed(1);
  const errorRateIsHigh = overview?.errorRateIsHigh ?? false;
  const addedCount = proxy.headers.length + proxy.query.length + proxy.bodyMerge.length;
  const addedSummary = [
    pluralize(proxy.headers.length, "header"),
    pluralize(proxy.query.length, "param"),
    pluralize(proxy.bodyMerge.length, "body field"),
  ].join(" · ");

  const toggleRevealedValue = (section: ProxyValueSection, id: string) => {
    setRevealedValues((current) => ({
      ...current,
      [section]: {
        ...current[section],
        [id]: !current[section]?.[id],
      },
    }));
  };

  const handleToggleEnabled = async () => {
    const enabled = !proxy.enabled;
    const res = await toggleProxy.mutateAsync({ id: proxy.id, enabled });
    if (!res.isSuccess)
      return showErrorToast({
        errors: res.errors || (enabled ? "Failed to resume proxy" : "Failed to pause proxy"),
      });
    showSuccessToast({ description: enabled ? "Proxy resumed." : "Proxy paused." });
  };

  return (
    <div className="flex min-h-screen flex-col">
      <div className="px-6 pb-2 pt-4">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="flex-1 space-y-6 px-6 pb-8 pt-4">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex flex-wrap items-center gap-3">
            <span
              aria-label={`${proxy.enabled ? "Live" : "Paused"} status indicator`}
              className={cn(
                "h-3.5 w-3.5 rounded-full",
                proxy.enabled ? "bg-success" : "bg-slate-400 dark:bg-slate-500",
              )}
            />
            <h1 className="text-2xl font-bold tracking-tight">{proxy.name}</h1>
            <ProxyStatusBadge proxy={proxy} />
          </div>
          <div className="flex flex-wrap gap-3">
            <Button
              type="button"
              variant="outline"
              disabled={toggleProxy.isPending}
              onClick={handleToggleEnabled}
            >
              {toggleProxy.isPending ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : proxy.enabled ? (
                <Pause className="mr-2 h-4 w-4" />
              ) : (
                <Play className="mr-2 h-4 w-4" />
              )}
              {toggleProxy.isPending ? pendingToggleLabel : toggleLabel}
            </Button>
            <Button onClick={() => navigate(scoped(`proxy/${proxy.id}/edit`))}>
              <Pen className="mr-2 h-4 w-4" />
              Edit
            </Button>
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-4">
          <div className="border-b border-border">
            <TabsList className="h-auto justify-start gap-8 rounded-none bg-transparent p-0">
              <TabsTrigger value="overview" className={tabClass}>
                Overview
              </TabsTrigger>
              <TabsTrigger value="logs" className={tabClass}>
                Request logs
              </TabsTrigger>
              <TabsTrigger value="history" className={tabClass}>
                Change history
              </TabsTrigger>
            </TabsList>
          </div>

          <TabsContent value="overview" className="space-y-6">
            {isLoadingOverview ? (
              <ProxyOverviewSkeleton />
            ) : (
              <>
                <div className="grid gap-4 md:grid-cols-3">
                  <MetricCard
                    label="Calls 24h"
                    value={calls24h.toLocaleString()}
                    note="through the proxy"
                  />
                  <MetricCard
                    label="Avg latency"
                    value={`${averageLatency} ms`}
                    note="end to end"
                  />
                  <MetricCard
                    label="Error rate"
                    value={`${errorRate}%`}
                    note="4xx + 5xx"
                    danger={errorRateIsHigh}
                  />
                </div>

                <Card className="rounded-xl">
                  <CardHeader>
                    <h2 className="text-xl font-semibold">Configuration</h2>
                  </CardHeader>
                  <CardContent className="space-y-6">
                    <div className="grid gap-4 lg:grid-cols-3">
                      <ConfigurationStepCard eyebrow="Your client calls">
                        <div className="mb-3 flex flex-wrap gap-2">
                          <ProxyMethodChips methods={proxy.methods} />
                        </div>
                        <p className="break-all font-mono text-sm text-foreground">
                          {getProxyClientPath(proxy.slug)}
                        </p>
                      </ConfigurationStepCard>
                      <ConfigurationStepCard eyebrow="→ Blocks adds" active>
                        <p className="text-3xl font-bold leading-none">{addedCount}</p>
                        <p className="mt-3 text-sm text-primary">{addedSummary}</p>
                      </ConfigurationStepCard>
                      <ConfigurationStepCard eyebrow="→ Third party receives">
                        <div className="flex items-start gap-2">
                          <p className="min-w-0 break-all font-mono text-sm text-foreground">
                            {showUpstream ? proxy.upstreamUrl : proxy.upstreamMasked}
                          </p>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
                            aria-label={`${showUpstream ? "Hide" : "Reveal"} third party endpoint`}
                            title={`${showUpstream ? "Hide" : "Reveal"} third party endpoint`}
                            onClick={() => setShowUpstream((value) => !value)}
                          >
                            {showUpstream ? (
                              <EyeOff className="h-4 w-4" />
                            ) : (
                              <Eye className="h-4 w-4" />
                            )}
                          </Button>
                        </div>
                        <p className="mt-3 text-sm text-muted-foreground">
                          Body: {pluralize(proxy.bodyMerge.length, "field")} merged server-side
                        </p>
                      </ConfigurationStepCard>
                    </div>
                    <KeyValueRows
                      section="headers"
                      title="Headers"
                      rows={proxy.headers}
                      empty="No headers added."
                      revealed={revealedValues}
                      onToggle={toggleRevealedValue}
                    />
                    <KeyValueRows
                      section="query"
                      title="Query parameters"
                      rows={proxy.query}
                      empty="No query parameters added."
                      revealed={revealedValues}
                      onToggle={toggleRevealedValue}
                    />
                    <KeyValueRows
                      section="body"
                      title="Body fields"
                      rows={proxy.bodyMerge}
                      empty="No body fields merged."
                      revealed={revealedValues}
                      onToggle={toggleRevealedValue}
                    />
                  </CardContent>
                </Card>
              </>
            )}
          </TabsContent>
          <TabsContent value="logs">
            <ProxyLogsTab proxy={proxy} active={activeTab === "logs"} />
          </TabsContent>
          <TabsContent value="history">
            <ProxyHistoryTab proxyId={proxy.id} active={activeTab === "history"} />
          </TabsContent>
        </Tabs>
      </div>
    </div>
  );
};
