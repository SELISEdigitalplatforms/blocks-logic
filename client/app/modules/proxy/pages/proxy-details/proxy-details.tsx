import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Eye, EyeOff, Pen, Route, ShieldCheck } from "lucide-react";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { showErrorToast } from "@/hooks/use-toast";
import { getProxyClientPath } from "../../constants";
import { useGetProxyById } from "../../hooks";
import { ProxyKeyValue } from "../../types";
import { ProxyMethodChips } from "../../components/proxy-method-chips";
import { ProxyStatusBadge } from "../../components/proxy-status-badge";
import { ProxyLogsTab } from "../../components/proxy-logs-tab";
import { ProxyHistoryTab } from "../../components/proxy-history-tab";
import { ProxyTestPanel } from "../../components/proxy-test-panel";

const InjectedRows = ({ title, rows }: { title: string; rows: ProxyKeyValue[] }) => (
  <div>
    <h3 className="text-sm font-semibold">{title}</h3>
    {rows.length ? (
      <div className="mt-2 divide-y rounded-sm border">
        {rows.map((row) => (
          <div key={`${row.key}-${row.value}`} className="grid gap-2 px-3 py-2 text-sm sm:grid-cols-[1fr_1fr_auto]">
            <span className="font-mono font-medium">{row.key}</span>
            <span className="min-w-0 truncate font-mono text-muted-foreground">{row.value}</span>
            {row.isSecretRef ? <Badge variant="info">Vault</Badge> : null}
          </div>
        ))}
      </div>
    ) : (
      <p className="mt-2 text-sm text-muted-foreground">No injected values.</p>
    )}
  </div>
);

export const ProxyDetails = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const params = useParams<{ proxyId?: string }>();
  const proxyId = params.proxyId;
  const [showUpstream, setShowUpstream] = useState(false);
  const [activeTab, setActiveTab] = useState("overview");
  const { data: proxy, isLoading, isFetched } = useGetProxyById(proxyId);

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

  return (
    <div className="flex min-h-screen flex-col">
      <div className="px-6 pb-2 pt-4">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>
      <div className="flex-1 space-y-6 px-6 pb-8 pt-4">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-2xl font-bold tracking-tight">{proxy.name}</h1>
              <ProxyStatusBadge proxy={proxy} />
            </div>
            <p className="mt-1 break-all font-mono text-xs text-muted-foreground">
              {getProxyClientPath(proxy.slug)}
            </p>
          </div>
          <Button variant="outline" size="sm" className="gap-2" onClick={() => navigate(scoped(`proxy/${proxy.id}/edit`))}>
            <Pen className="h-4 w-4" />
            Edit
          </Button>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-5">
          <TabsList>
            <TabsTrigger value="overview">Overview</TabsTrigger>
            <TabsTrigger value="logs">Request logs</TabsTrigger>
            <TabsTrigger value="history">Change history</TabsTrigger>
          </TabsList>
          <TabsContent value="overview" className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Route className="h-4 w-4 text-primary" />
                  Route overview
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-5">
                <div>
                  <span className="text-xs font-medium uppercase text-muted-foreground">Client path</span>
                  <p className="mt-1 break-all rounded-sm border bg-muted/20 px-3 py-2 font-mono text-sm">
                    {getProxyClientPath(proxy.slug)}
                  </p>
                </div>
                <div>
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-xs font-medium uppercase text-muted-foreground">Upstream</span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="xs"
                      className="gap-1.5"
                      onClick={() => setShowUpstream((value) => !value)}
                    >
                      {showUpstream ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                      {showUpstream ? "Hide" : "Reveal"}
                    </Button>
                  </div>
                  <p className="mt-1 break-all rounded-sm border bg-muted/20 px-3 py-2 font-mono text-sm">
                    {showUpstream ? proxy.upstreamUrl : proxy.upstreamMasked}
                  </p>
                </div>
                <div>
                  <span className="text-xs font-medium uppercase text-muted-foreground">Methods</span>
                  <div className="mt-2">
                    <ProxyMethodChips methods={proxy.methods} />
                  </div>
                </div>
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-base">
                  <ShieldCheck className="h-4 w-4 text-primary" />
                  Injection
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-5">
                <InjectedRows title="Headers" rows={proxy.headers} />
                <InjectedRows title="Query parameters" rows={proxy.query} />
              </CardContent>
            </Card>
            <ProxyTestPanel proxyId={proxy.id} method={proxy.methods[0]} />
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
