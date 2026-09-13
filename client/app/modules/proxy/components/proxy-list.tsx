import { useState } from "react";
import { useNavigate } from "react-router";
import { useProjectStore, useScopedPath } from "@seliseblocks/genesis-os";
import { ArrowRightFromLine, EllipsisVertical, Pause, Pen, Play, Plus, Trash } from "lucide-react";
import { ProxyIcon } from "@/constants/navigation-menus";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Proxy } from "../types";
import { useToggleProxy } from "../hooks";
import { getProxyClientUrl } from "../constants";
import { DeleteProxyDialog } from "./delete-proxy-dialog";
import { ProxyMethodBadge } from "./proxy-method-badge";
import { VariablesButton } from "./variables-button";

type Props = {
  proxies: Proxy[];
  isLoading: boolean;
  /** Lets the list screen step back a page when the deleted row was the page's last one. */
  onProxyDeleted?: () => void;
};

const VISIBLE_METHOD_COUNT = 2;

const ProxyListSkeleton = () => (
  <>
    {Array.from({ length: 10 }).map((_, index) => (
      <TableRow key={index} className="border-0">
        <TableCell colSpan={6} className="rounded-lg border border-border bg-background p-4">
          <Skeleton className="h-12 w-full" />
        </TableCell>
      </TableRow>
    ))}
  </>
);

export const ProxyList = ({ proxies, isLoading, onProxyDeleted }: Props) => {
  const navigate = useNavigate();
  const selectedProject = useProjectStore().selectedProject;
  const scoped = useScopedPath();
  const toggleProxy = useToggleProxy();
  const [proxyToDelete, setProxyToDelete] = useState<Proxy | null>(null);

  const handleToggle = async (proxy: Proxy, enabled: boolean) => {
    const res = await toggleProxy.mutateAsync({ id: proxy.id, enabled });
    if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to update proxy" });
    showSuccessToast({ description: enabled ? "Proxy enabled." : "Proxy paused." });
  };

  if (isLoading) {
    return (
      <Table className="border-separate border-spacing-y-4">
        <TableBody className="[&_tr:last-child]:border-0">
          <ProxyListSkeleton />
        </TableBody>
      </Table>
    );
  }

  if (!proxies.length) {
    return (
      <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
          <ProxyIcon className="h-7 w-7" />
        </div>
        <h3 className="mt-5 text-lg font-semibold text-high-emphasis">No proxies yet</h3>
        <p className="mt-2 max-w-md text-sm text-muted-foreground">
          Create your first proxy configuration to keep client traffic stable while upstream
          services stay behind a managed route.
        </p>
        <div className="mt-6 flex flex-col items-center gap-2 sm:flex-row">
          <VariablesButton className="hidden" />
          <Button className="gap-2" size="sm" onClick={() => navigate(scoped("proxy/new"))}>
            <Plus className="h-4 w-4" />
            Add proxy
          </Button>
        </div>
      </div>
    );
  }

  return (
    <>
      <Table className="min-w-[920px] border-separate border-spacing-y-4">
        <TableHeader className="[&_tr]:border-0">
          <TableRow className="border-0">
            <TableHead className="px-6 pb-0 pt-2 text-base">
              <div className="font-bold text-medium-emphasis">Name</div>
            </TableHead>
            <TableHead className="px-6 pb-0 pt-2 text-base">
              <div className="font-bold text-medium-emphasis">Client route</div>
            </TableHead>
            <TableHead className="w-[220px] px-6 pb-0 pt-2 text-base">
              <div className="font-bold text-medium-emphasis">Methods</div>
            </TableHead>
            <TableHead className="px-6 pb-0 pt-2 text-base">
              <div className="font-bold text-medium-emphasis">Upstream</div>
            </TableHead>
            <TableHead className="px-6 pb-0 pt-2 text-base">
              <div className="font-bold text-medium-emphasis">Traffic</div>
            </TableHead>
            <TableHead className="w-[160px] px-6 pb-0 pt-2 text-base">
              <div className="font-bold text-medium-emphasis">Active</div>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody className="[&_tr:last-child]:border-0">
          {proxies.map((proxy) => (
            <TableRow
              key={proxy.id}
              className="group cursor-pointer border-0 transition-colors"
              onClick={() => navigate(scoped(`proxy/${proxy.id}`))}
            >
              <TableCell className="rounded-l-lg border-y border-l border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50">
                <div className="w-[200px] truncate font-semibold md:w-[240px]">{proxy.name}</div>
              </TableCell>
              <TableCell className="border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50">
                <p className="w-[260px] truncate font-mono text-sm text-muted-foreground">
                  {getProxyClientUrl(selectedProject, proxy.slug)}
                </p>
              </TableCell>
              <TableCell className="w-[220px] border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50">
                <div className="flex min-w-[170px] items-center gap-1.5 whitespace-nowrap">
                  {proxy.methods.slice(0, VISIBLE_METHOD_COUNT).map((method) => (
                    <ProxyMethodBadge key={method} method={method} />
                  ))}
                  {proxy.methods.length > VISIBLE_METHOD_COUNT ? (
                    <span
                      className="w-fit rounded border border-border bg-muted px-2 py-1 font-mono text-[11px] font-bold leading-none text-muted-foreground"
                      title={proxy.methods.slice(VISIBLE_METHOD_COUNT).join(", ")}
                    >
                      +{proxy.methods.length - VISIBLE_METHOD_COUNT} more
                    </span>
                  ) : null}
                </div>
              </TableCell>
              <TableCell className="border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50">
                <p className="w-[220px] truncate text-sm text-muted-foreground">
                  {proxy.upstreamMasked}
                </p>
              </TableCell>
              <TableCell className="border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50">
                <span className="whitespace-nowrap text-sm text-muted-foreground">
                  {proxy.calls24h.toLocaleString()} calls 24h
                </span>
              </TableCell>
              <TableCell className="w-[160px] rounded-r-lg border-y border-r border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50">
                <div
                  className="flex items-center justify-start gap-8"
                  onClick={(event) => event.stopPropagation()}
                >
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Switch
                      aria-label={`${proxy.name} enabled`}
                      checked={proxy.enabled}
                      disabled={toggleProxy.isPending}
                      onCheckedChange={(checked) => handleToggle(proxy, checked)}
                    />
                  </div>
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        className="h-5 w-5 p-0"
                        aria-label={`${proxy.name} options`}
                      >
                        <EllipsisVertical width={20} height={20} />
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
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
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
