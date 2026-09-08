import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Activity, Plus, Route } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Switch } from "@/components/ui-kits/switch/switch";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Proxy } from "../types";
import { useToggleProxy } from "../hooks";
import { getProxyClientPath } from "../constants";
import { ProxyMethodChips } from "./proxy-method-chips";
import { ProxyStatusBadge } from "./proxy-status-badge";

type Props = {
  proxies: Proxy[];
  isLoading: boolean;
};

const ProxyListSkeleton = () => (
  <div className="grid gap-3">
    {Array.from({ length: 4 }).map((_, index) => (
      <Card key={index} className="p-0">
        <CardContent className="flex flex-col gap-4 p-4 lg:flex-row lg:items-center lg:justify-between">
          <div className="min-w-0 flex-1 space-y-3">
            <div className="flex items-center gap-2">
              <Skeleton className="h-5 w-40" />
              <Skeleton className="h-5 w-16" />
            </div>
            <Skeleton className="h-4 w-full max-w-sm" />
            <div className="flex items-center gap-2">
              <Skeleton className="h-6 w-12" />
              <Skeleton className="h-4 w-full max-w-md" />
            </div>
          </div>
          <div className="flex items-center justify-between gap-5 lg:justify-end">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-6 w-10 rounded-full" />
          </div>
        </CardContent>
      </Card>
    ))}
  </div>
);

export const ProxyList = ({ proxies, isLoading }: Props) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const toggleProxy = useToggleProxy();

  const handleToggle = async (proxy: Proxy, enabled: boolean) => {
    const res = await toggleProxy.mutateAsync({ id: proxy.id, enabled });
    if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to update proxy" });
    showSuccessToast({ description: enabled ? "Proxy enabled." : "Proxy paused." });
  };

  if (isLoading) {
    return <ProxyListSkeleton />;
  }

  if (!proxies.length) {
    return (
      <Card className="p-0">
        <CardContent className="flex min-h-[360px] flex-col items-center justify-center px-6 py-12 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
            <Route className="h-7 w-7" />
          </div>
          <h3 className="mt-5 text-xl font-semibold">No proxies yet</h3>
          <p className="mt-2 max-w-md text-sm text-muted-foreground">
            Create your first proxy configuration to keep client traffic stable while upstream
            services stay behind a managed route.
          </p>
          <Button className="mt-5 gap-2" onClick={() => navigate(scoped("proxy/new"))}>
            <Plus className="h-4 w-4" />
            Add proxy
          </Button>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="grid gap-3">
      {proxies.map((proxy) => (
        <Card key={proxy.id} className="rounded-xl p-0">
          <CardContent className="flex flex-col gap-4 p-4 lg:flex-row lg:items-center lg:justify-between">
            <button
              type="button"
              className="min-w-0 flex-1 text-left"
              onClick={() => navigate(scoped(`proxy/${proxy.id}`))}
            >
              <div className="min-w-0 space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <h3 className="text-base font-semibold">{proxy.name}</h3>
                  <ProxyStatusBadge proxy={proxy} />
                </div>
                <p className="truncate font-mono text-xs text-muted-foreground">
                  {getProxyClientPath(proxy.slug)}
                </p>
                <div className="flex min-w-0 flex-wrap items-center gap-2">
                  <ProxyMethodChips methods={proxy.methods} />
                  <p className="min-w-0 truncate text-sm text-muted-foreground">
                    {proxy.upstreamMasked}
                  </p>
                </div>
              </div>
            </button>
            <div className="flex items-center justify-between gap-5 lg:justify-end">
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <Activity className="h-4 w-4" />
                <span>{proxy.calls24h.toLocaleString()} calls 24h</span>
              </div>
              <Switch
                aria-label={`${proxy.name} enabled`}
                checked={proxy.enabled}
                disabled={toggleProxy.isPending}
                onCheckedChange={(checked) => handleToggle(proxy, checked)}
              />
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
};
