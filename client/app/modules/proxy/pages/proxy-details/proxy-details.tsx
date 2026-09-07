import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Eye, EyeOff, Pause, Pen } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { getProxyClientPath } from "../../constants";
import {
  useGetProxyById,
  useGetProxyExecutions,
  useGetProxyVersions,
  useToggleProxy,
} from "../../hooks";
import { ProxyKeyValue } from "../../types";
import { ProxyMethodChips } from "../../components/proxy-method-chips";
import { ProxyStatusBadge } from "../../components/proxy-status-badge";
import { ProxyLogsTab } from "../../components/proxy-logs-tab";
import { ProxyHistoryTab } from "../../components/proxy-history-tab";

type InjectedRow = ProxyKeyValue & { type: "header" | "query" };

const InjectedRows = ({ title, rows }: { title: string; rows: InjectedRow[] }) => (
  <div>
    <h3 className="text-xs font-semibold uppercase text-muted-foreground">{title}</h3>
    {rows.length ? (
      <div className="mt-2 divide-y overflow-hidden rounded-lg border">
        {rows.map((row) => (
          <div
            key={`${row.key}-${row.value}`}
            className="grid gap-2 px-4 py-3 text-sm sm:grid-cols-[auto_1fr_auto] sm:items-center"
          >
            <Badge variant="info" className="w-fit lowercase">
              {row.type}
            </Badge>
            <span className="min-w-0 truncate">
              <span className="font-mono font-medium">{row.key}</span>
              <span className="mx-2 font-mono text-muted-foreground">{row.value}</span>
            </span>
            {row.isSecretRef ? (
              <Badge variant="success" className="w-fit lowercase">
                vault
              </Badge>
            ) : null}
          </div>
        ))}
      </div>
    ) : (
      <p className="mt-2 text-sm text-muted-foreground">No injected values.</p>
    )}
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

const tabClass =
  "rounded-none border-b-2 border-transparent px-0 pb-4 pt-0 text-base data-[state=active]:border-primary data-[state=active]:bg-transparent data-[state=active]:text-primary data-[state=active]:shadow-none";

export const ProxyDetails = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const params = useParams<{ proxyId?: string }>();
  const proxyId = params.proxyId;
  const [showUpstream, setShowUpstream] = useState(false);
  const [activeTab, setActiveTab] = useState("overview");
  const { data: proxy, isLoading, isFetched } = useGetProxyById(proxyId);
  const { data: allLogs = [] } = useGetProxyExecutions(proxy?.id, "all", {
    enabled: Boolean(proxy),
  });
  const { data: versions = [] } = useGetProxyVersions(proxy?.id);
  const toggleProxy = useToggleProxy();

  useEffect(() => {
    if (proxyId && isFetched && !isLoading && !proxy) {
      showErrorToast({ errors: "Proxy not found" });
      navigate(scoped("proxy"));
    } else if (proxy?.name && proxyId) {
      BREADCRUMB_CUSTOM_TITLES[`/proxy/${proxyId}`] = proxy.name;
    }
  }, [isFetched, isLoading, navigate, proxy, proxyId, scoped]);

  if (isLoading || !isFetched) {
    return <div className="p-8 text-sm text-muted-foreground">Loading proxy...</div>;
  }

  if (!proxy) return null;

  const averageLatency = allLogs.length
    ? Math.round(allLogs.reduce((sum, log) => sum + log.latencyMs, 0) / allLogs.length)
    : 0;
  const errorCount = allLogs.filter((log) => log.status >= 400).length;
  const errorRate = allLogs.length ? ((errorCount / allLogs.length) * 100).toFixed(1) : "0.0";
  const credentialLabel = [...proxy.headers, ...proxy.query].some((row) => row.isSecretRef)
    ? "vault"
    : "none";

  const handlePause = async () => {
    const res = await toggleProxy.mutateAsync({ id: proxy.id, enabled: false });
    if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to pause proxy" });
    showSuccessToast({ description: "Proxy paused." });
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
              className={cn(
                "h-3.5 w-3.5 rounded-full",
                proxy.enabled ? "bg-success" : "bg-destructive",
              )}
            />
            <h1 className="text-2xl font-bold tracking-tight">{proxy.name}</h1>
            <ProxyStatusBadge proxy={proxy} />
          </div>
          <div className="flex flex-wrap gap-3">
            <Button
              type="button"
              variant="outline"
              disabled={!proxy.enabled || toggleProxy.isPending}
              onClick={handlePause}
            >
              <Pause className="mr-2 h-4 w-4" />
              Pause
            </Button>
            <Button onClick={() => navigate(scoped(`proxy/${proxy.id}/edit`))}>
              <Pen className="mr-2 h-4 w-4" />
              Edit
            </Button>
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-7">
          <div className="border-b border-border">
            <TabsList className="h-auto justify-start gap-8 rounded-none bg-transparent p-0">
              <TabsTrigger value="overview" className={tabClass}>
                Overview
              </TabsTrigger>
              <TabsTrigger value="logs" className={cn(tabClass, "gap-3")}>
                Request logs
                <span className="text-sm text-muted-foreground">{allLogs.length}</span>
              </TabsTrigger>
              <TabsTrigger value="history" className={cn(tabClass, "gap-3")}>
                Change history
                <Badge variant="info" className="rounded-full px-2">
                  {versions.length}
                </Badge>
              </TabsTrigger>
            </TabsList>
          </div>

          <TabsContent value="overview" className="space-y-6">
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <MetricCard
                label="Calls 24h"
                value={proxy.calls24h.toLocaleString()}
                note="through the proxy"
              />
              <MetricCard label="Avg latency" value={`${averageLatency} ms`} note="end to end" />
              <MetricCard
                label="Error rate"
                value={`${errorRate}%`}
                note="4xx + 5xx"
                danger={errorCount > 0}
              />
              <MetricCard label="Credential" value={credentialLabel} note="${SECRET.*}" />
            </div>

            <Card className="rounded-xl">
              <CardHeader>
                <h2 className="text-xl font-semibold">Configuration</h2>
              </CardHeader>
              <CardContent className="space-y-5">
                <div className="space-y-3 rounded-lg border border-primary/25 bg-primary/5 p-4 text-primary">
                  <div>
                    <span className="text-xs font-semibold uppercase tracking-wide">
                      Your client calls
                    </span>
                    <p className="mt-1 break-all font-mono text-sm text-foreground">
                      {getProxyClientPath(proxy.slug)}
                    </p>
                  </div>
                  <div>
                    <span className="text-xs font-semibold uppercase tracking-wide">
                      Blocks forwards to
                    </span>
                    <p className="mt-1 break-all font-mono text-sm text-foreground">
                      {showUpstream ? proxy.upstreamUrl : proxy.upstreamMasked}
                      <Button
                        type="button"
                        variant="ghost"
                        size="xs"
                        className="ml-2 h-auto px-1 py-0 text-primary"
                        onClick={() => setShowUpstream((value) => !value)}
                      >
                        {showUpstream ? (
                          <EyeOff className="mr-1 h-3.5 w-3.5" />
                        ) : (
                          <Eye className="mr-1 h-3.5 w-3.5" />
                        )}
                        {showUpstream ? "hide" : "reveal"}
                      </Button>
                    </p>
                  </div>
                </div>
                <div>
                  <span className="text-xs font-semibold uppercase text-muted-foreground">
                    Methods
                  </span>
                  <div className="mt-2">
                    <ProxyMethodChips methods={proxy.methods} />
                  </div>
                </div>
                <InjectedRows
                  title="Headers & parameters sent"
                  rows={[
                    ...proxy.headers.map((row) => ({ ...row, type: "header" as const })),
                    ...proxy.query.map((row) => ({ ...row, type: "query" as const })),
                  ]}
                />
              </CardContent>
            </Card>
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
