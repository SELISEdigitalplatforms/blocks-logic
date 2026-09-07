import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Activity, Plus, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
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
    return <div className="py-16 text-center text-sm text-muted-foreground">Loading proxies...</div>;
  }

  if (!proxies.length) {
    return (
      <div className="flex min-h-[360px] flex-col items-center justify-center text-center">
        <ShieldCheck className="h-12 w-12 text-primary" />
        <h3 className="mt-5 text-xl font-semibold">No proxies yet</h3>
        <p className="mt-2 max-w-md text-sm text-muted-foreground">
          Create your first proxy configuration to keep client traffic stable while upstream
          services stay behind a managed route.
        </p>
        <Button className="mt-5 gap-2" onClick={() => navigate(scoped("proxy/new"))}>
          <Plus className="h-4 w-4" />
          Add proxy
        </Button>
      </div>
    );
  }

  return (
    <div className="grid gap-3">
      {proxies.map((proxy) => (
        <Card key={proxy.id} className="p-0">
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
                  <p className="truncate text-sm text-muted-foreground">{proxy.upstreamMasked}</p>
                  <ProxyMethodChips methods={proxy.methods} />
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
