import { useState } from "react";
import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import {
  Activity,
  ArrowRight,
  ArrowRightFromLine,
  EllipsisVertical,
  Pause,
  Pen,
  Play,
  Plus,
  Trash,
} from "lucide-react";
import { ProxyIcon } from "@/constants/navigation-menus";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Switch } from "@/components/ui-kits/switch/switch";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Proxy } from "../types";
import { useToggleProxy } from "../hooks";
import { getProxyClientPath } from "../constants";
import { DeleteProxyDialog } from "./delete-proxy-dialog";
import { ProxyMethodChips } from "./proxy-method-chips";
import { ProxyStatusBadge } from "./proxy-status-badge";
import { VariablesButton } from "./variables-button";

type Props = {
  proxies: Proxy[];
  isLoading: boolean;
  /** Lets the list screen step back a page when the deleted row was the page's last one. */
  onProxyDeleted?: () => void;
};

const ProxyListSkeleton = () => (
  <div className="grid gap-2">
    {Array.from({ length: 4 }).map((_, index) => (
      <Card key={index} className="p-0">
        <CardContent className="flex flex-col gap-3 px-4 py-3 lg:flex-row lg:items-center lg:justify-between">
          <div className="min-w-0 flex-1 space-y-2">
            <div className="flex items-center gap-2">
              <Skeleton className="h-5 w-40" />
              <Skeleton className="h-5 w-14" />
            </div>
            <div className="flex items-center gap-2">
              <Skeleton className="h-4 w-12" />
              <Skeleton className="h-4 w-full max-w-md" />
            </div>
          </div>
          <div className="flex items-center justify-between gap-4 lg:justify-end">
            <Skeleton className="h-4 w-20" />
            <Skeleton className="h-5 w-9 rounded-full" />
            <Skeleton className="h-8 w-8" />
          </div>
        </CardContent>
      </Card>
    ))}
  </div>
);

export const ProxyList = ({ proxies, isLoading, onProxyDeleted }: Props) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const toggleProxy = useToggleProxy();
  const [proxyToDelete, setProxyToDelete] = useState<Proxy | null>(null);

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
            <ProxyIcon className="h-7 w-7" />
          </div>
          <h3 className="mt-5 text-xl font-semibold">No proxies yet</h3>
          <p className="mt-2 max-w-md text-sm text-muted-foreground">
            Create your first proxy configuration to keep client traffic stable while upstream
            services stay behind a managed route.
          </p>
          <div className="mt-5 flex flex-col items-center gap-2 sm:flex-row">
            <VariablesButton className="hidden" />
            <Button className="gap-2" onClick={() => navigate(scoped("proxy/new"))}>
              <Plus className="h-4 w-4" />
              Add proxy
            </Button>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <>
      <div className="grid gap-2">
        {proxies.map((proxy) => (
          <Card
            key={proxy.id}
            className="group rounded-lg p-0 transition-colors duration-150 hover:border-primary/40 focus-within:border-primary/40"
          >
            <CardContent className="flex flex-col gap-2 px-4 py-3 lg:flex-row lg:items-center lg:justify-between lg:gap-6">
              <button
                type="button"
                className="min-w-0 flex-1 rounded-md text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                onClick={() => navigate(scoped(`proxy/${proxy.id}`))}
              >
                <div className="flex min-w-0 flex-wrap items-center gap-2">
                  <h3 className="truncate text-sm font-semibold transition-colors group-hover:text-primary group-focus-within:text-primary">
                    {proxy.name}
                  </h3>
                  <ProxyStatusBadge proxy={proxy} />
                </div>
                <div className="mt-1 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
                  <ProxyMethodChips methods={proxy.methods} />
                  <span className="min-w-0 truncate font-mono">
                    {getProxyClientPath(proxy.slug)}
                  </span>
                  <ArrowRight className="h-3 w-3 shrink-0 opacity-60" aria-hidden="true" />
                  <span className="min-w-0 truncate">{proxy.upstreamMasked}</span>
                </div>
              </button>
              <div className="flex shrink-0 items-center justify-between gap-4 lg:justify-end">
                <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                  <Activity className="h-3.5 w-3.5" />
                  <span>{proxy.calls24h.toLocaleString()} calls 24h</span>
                </div>
                <Switch
                  aria-label={`${proxy.name} enabled`}
                  checked={proxy.enabled}
                  disabled={toggleProxy.isPending}
                  onCheckedChange={(checked) => handleToggle(proxy, checked)}
                />
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button
                      variant="ghost"
                      className="h-8 w-8 p-0"
                      aria-label={`${proxy.name} options`}
                    >
                      <EllipsisVertical className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem
                      className="cursor-pointer"
                      onClick={() => navigate(scoped(`proxy/${proxy.id}`))}
                    >
                      <ArrowRightFromLine className="mr-2 h-4 w-4" />
                      <span>Open</span>
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      className="cursor-pointer"
                      onClick={() => navigate(scoped(`proxy/${proxy.id}/edit`))}
                    >
                      <Pen className="mr-2 h-4 w-4" />
                      <span>Edit</span>
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      className="cursor-pointer"
                      disabled={toggleProxy.isPending}
                      onClick={() => handleToggle(proxy, !proxy.enabled)}
                    >
                      {proxy.enabled ? (
                        <Pause className="mr-2 h-4 w-4" />
                      ) : (
                        <Play className="mr-2 h-4 w-4" />
                      )}
                      <span>{proxy.enabled ? "Disable" : "Enable"}</span>
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      className="cursor-pointer text-destructive focus:text-destructive"
                      onClick={() => setProxyToDelete(proxy)}
                    >
                      <Trash className="mr-2 h-4 w-4" />
                      <span>Delete</span>
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>
      <DeleteProxyDialog
        open={Boolean(proxyToDelete)}
        onOpenChange={(value) => {
          if (!value) setProxyToDelete(null);
        }}
        proxy={proxyToDelete}
        onDeleted={onProxyDeleted}
      />
    </>
  );
};
