import { type ReactNode, useEffect, useState } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { useProjectStore, useScopedPath } from "@seliseblocks/genesis-os";
import { EllipsisVertical, Eye, EyeOff, Loader2, Pause, Pen, Play, Trash2 } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { getProxyClientUrl } from "../../constants";
import { useGetProxyById, useGetProxyOverview, useToggleProxy } from "../../hooks";
import { Proxy, ProxyKeyValue, ResponseFieldNode } from "../../types";
import { containsVarRef, pathsToTree } from "../../utils";
import { ProxyMethodChips } from "../../components/proxy-method-chips";
import { ProxyStatusBadge } from "../../components/proxy-status-badge";
import { ProxyLogsTab } from "../../components/proxy-logs-tab";
import { ProxyHistoryTab } from "../../components/proxy-history-tab";
import { ProxyTestTab } from "../../components/proxy-test-tab";
import { DeleteProxyDialog } from "../../components/delete-proxy-dialog";

const pluralize = (count: number, singular: string, plural = `${singular}s`) =>
  `${count} ${count === 1 ? singular : plural}`;

const KeyValueRows = ({
  title,
  rows,
  empty,
}: {
  title: string;
  rows: ProxyKeyValue[];
  empty: string;
}) => (
  <div>
    <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</h3>
    <div className="mt-3 overflow-hidden rounded-lg border bg-card">
      <div className="border-b bg-muted/20 px-4 py-3 text-sm font-semibold text-muted-foreground">
        {title} · {rows.length}
      </div>
      {rows.length ? (
        <div className="divide-y">
          {rows.map((row, index) => (
            <div
              key={`${row.key}-${index}`}
              className="grid gap-3 px-4 py-3 text-sm sm:grid-cols-[minmax(160px,0.4fr)_minmax(0,1fr)] sm:items-center"
            >
              <div className="flex min-w-0 items-center gap-2">
                <span className="truncate font-mono font-semibold text-foreground">{row.key}</span>
                {containsVarRef(row.value) ? (
                  <Badge variant="success" className="w-fit shrink-0 lowercase">
                    variable
                  </Badge>
                ) : null}
              </div>
              {/* Values are shown as configured: a variable reference already hides the secret behind its name. */}
              <span className="min-w-0 break-all font-mono text-muted-foreground">{row.value}</span>
            </div>
          ))}
        </div>
      ) : (
        <p className="px-4 py-3 text-sm text-muted-foreground">{empty}</p>
      )}
    </div>
  </div>
);

/** Read-only "Response filtering" block for the config panel (SPEC §5.7). */
const ResponseFilterTree = ({
  nodes,
  depth = 0,
}: {
  nodes: ResponseFieldNode[];
  depth?: number;
}) => (
  <ul className={cn("space-y-1", depth > 0 && "mt-1")}>
    {nodes.map((node) => (
      <li key={node.id}>
        <div className="flex items-center gap-2" style={{ paddingLeft: depth * 16 }}>
          <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-muted-foreground/50" />
          <span className="font-mono text-xs text-foreground">
            {node.key}
            {node.isList ? "[]" : ""}
          </span>
        </div>
        {node.children.length ? (
          <ResponseFilterTree nodes={node.children} depth={depth + 1} />
        ) : null}
      </li>
    ))}
  </ul>
);

const ResponseFilterSection = ({ proxy }: { proxy: Proxy }) => {
  const isSelect = proxy.responseMode === "select";
  const paths = proxy.responseInclude ?? [];
  const { tree } = pathsToTree(paths);
  return (
    <div>
      <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        Response filtering
      </h3>
      <div className="mt-3 rounded-lg border bg-card p-4 text-sm">
        {!isSelect ? (
          <p className="text-muted-foreground">Sends the full upstream response.</p>
        ) : paths.length ? (
          <div className="space-y-2">
            <p className="text-muted-foreground">Forwards {pluralize(paths.length, "field")}:</p>
            <ResponseFilterTree nodes={tree} />
          </div>
        ) : (
          <p className="text-muted-foreground">
            Forwards an empty object (<code>{"{}"}</code>) — no fields selected yet.
          </p>
        )}
      </div>
    </div>
  );
};

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
      <p className={cn("mt-2 text-2xl font-bold", danger ? "text-destructive" : "text-foreground")}>
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

const proxyDetailTabs = [
  { value: "overview", label: "Overview" },
  { value: "test", label: "Test" },
  { value: "logs", label: "Request logs" },
  { value: "history", label: "Change history" },
];

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
  const selectedProject = useProjectStore().selectedProject;
  const scoped = useScopedPath();
  const { pathname } = useLocation();
  const params = useParams<{ proxyId?: string }>();
  const proxyId = params.proxyId;
  const [showUpstream, setShowUpstream] = useState(false);
  const [activeTab, setActiveTab] = useState("overview");
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
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
    } else if (proxy?.name) {
      BREADCRUMB_CUSTOM_TITLES[pathname] = proxy.name;
    }
  }, [isFetched, isLoading, navigate, pathname, proxy, proxyId, scoped]);

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
        <div className="flex items-center justify-between gap-4 sm:items-start mb-3">
          <div className="flex min-w-0 flex-1 items-center gap-3">
            <span
              aria-label={`${proxy.enabled ? "Live" : "Paused"} status indicator`}
              className={cn(
                "h-2 w-2 shrink-0 rounded-full",
                proxy.enabled ? "bg-success" : "bg-slate-400 dark:bg-slate-500",
              )}
            />
            <h1 className="min-w-0 truncate text-2xl font-bold tracking-tight">{proxy.name}</h1>
            <div className="shrink-0">
              <ProxyStatusBadge proxy={proxy} />
            </div>
          </div>
          <div className="shrink-0 sm:hidden">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="icon" aria-label="Proxy actions">
                  <EllipsisVertical className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem
                  className="cursor-pointer"
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
                  <span>{toggleProxy.isPending ? pendingToggleLabel : toggleLabel}</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={() => navigate(scoped(`proxy/${proxy.id}/edit`))}
                >
                  <Pen className="mr-2 h-4 w-4" />
                  <span>Edit</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer text-destructive focus:text-destructive"
                  disabled={toggleProxy.isPending}
                  onClick={() => setIsDeleteOpen(true)}
                >
                  <Trash2 className="mr-2 h-4 w-4" />
                  <span>Delete</span>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
          <div className="hidden flex-wrap gap-3 sm:flex">
            <Button
              type="button"
              variant="outline"
              disabled={toggleProxy.isPending}
              aria-label={toggleProxy.isPending ? pendingToggleLabel : toggleLabel}
              title={toggleProxy.isPending ? pendingToggleLabel : toggleLabel}
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
            <Button
              aria-label="Edit"
              title="Edit"
              onClick={() => navigate(scoped(`proxy/${proxy.id}/edit`))}
            >
              <Pen className="mr-2 h-4 w-4" />
              Edit
            </Button>
            <Button
              type="button"
              variant="outline"
              className="gap-2 text-destructive hover:text-destructive"
              disabled={toggleProxy.isPending}
              aria-label="Delete"
              title="Delete"
              onClick={() => setIsDeleteOpen(true)}
            >
              <Trash2 className="h-4 w-4" />
              Delete
            </Button>
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-4 !mt-0">
          <div>
            <div className="sm:hidden">
              <Select value={activeTab} onValueChange={setActiveTab}>
                <SelectTrigger aria-label="Proxy detail section">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {proxyDetailTabs.map((tab) => (
                    <SelectItem key={tab.value} value={tab.value}>
                      {tab.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <TabsList className="hidden sm:inline-flex">
              {proxyDetailTabs.map((tab) => (
                <TabsTrigger key={tab.value} value={tab.value}>
                  {tab.label}
                </TabsTrigger>
              ))}
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
                    <h2 className="text-lg font-semibold">Configuration</h2>
                  </CardHeader>
                  <CardContent className="space-y-6">
                    <div className="grid gap-4 lg:grid-cols-3">
                      <ConfigurationStepCard eyebrow="Your client calls">
                        <div className="mb-3 flex flex-wrap gap-2">
                          <ProxyMethodChips methods={proxy.methods} />
                        </div>
                        <p className="break-all font-mono text-sm text-foreground">
                          {getProxyClientUrl(selectedProject, proxy.slug)}
                        </p>
                      </ConfigurationStepCard>
                      <ConfigurationStepCard eyebrow="→ Blocks adds" active>
                        <p className="text-2xl font-bold leading-none">{addedCount}</p>
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
                    <KeyValueRows title="Headers" rows={proxy.headers} empty="No headers added." />
                    <KeyValueRows
                      title="Query parameters"
                      rows={proxy.query}
                      empty="No query parameters added."
                    />
                    <KeyValueRows
                      title="Body fields"
                      rows={proxy.bodyMerge}
                      empty="No body fields merged."
                    />
                    <ResponseFilterSection proxy={proxy} />
                  </CardContent>
                </Card>
              </>
            )}
          </TabsContent>
          <TabsContent value="test">
            <ProxyTestTab proxy={proxy} />
          </TabsContent>
          <TabsContent value="logs">
            <ProxyLogsTab proxy={proxy} active={activeTab === "logs"} />
          </TabsContent>
          <TabsContent value="history">
            <ProxyHistoryTab proxyId={proxy.id} active={activeTab === "history"} />
          </TabsContent>
        </Tabs>
      </div>

      <DeleteProxyDialog
        open={isDeleteOpen}
        onOpenChange={setIsDeleteOpen}
        proxy={proxy}
        onDeleted={() => navigate(scoped("proxy"))}
      />
    </div>
  );
};
