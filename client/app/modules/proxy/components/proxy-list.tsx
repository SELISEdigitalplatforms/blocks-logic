import { useState } from "react";
import { useNavigate } from "react-router";
import { useProjectStore, useScopedPath } from "@seliseblocks/genesis-os";
import {
  AlertTriangle,
  ArrowRightFromLine,
  Download,
  EllipsisVertical,
  Pause,
  Pen,
  Play,
  Plus,
  Trash,
} from "lucide-react";
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
import { cn } from "@/lib/utils";
import { Proxy } from "../types";
import { useProxyExport, useToggleProxy } from "../hooks";
import { getProxyClientUrl } from "../constants";
import { DeleteProxyDialog } from "./delete-proxy-dialog";
import { ProxyMethodBadge } from "./proxy-method-badge";

type Props = {
  proxies: Proxy[];
  isLoading: boolean;
  /** Lets the list screen step back a page when the deleted row was the page's last one. */
  onProxyDeleted?: () => void;
  /** True when a search term or status filter is narrowing the list, so a zero-row result is
   * "no matches" rather than "nothing has ever been created" — the two need different copy. */
  isFiltered?: boolean;
  /** Skeleton rows, so the placeholder is as tall as the page that replaces it. */
  rowCount?: number;
};

const VISIBLE_METHOD_COUNT = 2;

const HEAD_CLASS = "px-6 pb-0 pt-2 text-base";

const CELL_CLASS =
  "border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50";

const SKELETON_ROWS = 10;

const ProxyListSkeleton = ({ rowCount }: { rowCount: number }) => (
  <>
    {Array.from({ length: rowCount }).map((_, index) => (
      <TableRow key={index} className="border-0">
        <TableCell colSpan={6} className="rounded-lg border border-border bg-background p-4">
          <Skeleton className="h-12 w-full" />
        </TableCell>
      </TableRow>
    ))}
  </>
);

export const ProxyList = ({
  proxies,
  isLoading,
  onProxyDeleted,
  isFiltered,
  rowCount = SKELETON_ROWS,
}: Props) => {
  const navigate = useNavigate();
  const selectedProject = useProjectStore().selectedProject;
  const scoped = useScopedPath();
  const toggleProxy = useToggleProxy();
  const proxyExport = useProxyExport();
  const [proxyToDelete, setProxyToDelete] = useState<Proxy | null>(null);

  const handleToggle = async (proxy: Proxy, enabled: boolean) => {
    const res = await toggleProxy.mutateAsync({ id: proxy.id, enabled });
    if (!res.isSuccess) return showErrorToast({ errors: res.errors || "Failed to update proxy" });
    showSuccessToast({ description: enabled ? "Proxy enabled." : "Proxy paused." });
  };

  if (isLoading) {
    return (
      <Table className="border-separate border-spacing-y-4" aria-busy="true" aria-live="polite">
        <TableBody className="[&_tr:last-child]:border-0">
          <ProxyListSkeleton rowCount={rowCount} />
        </TableBody>
      </Table>
    );
  }

  if (!proxies.length) {
    if (isFiltered) {
      return (
        <p className="py-14 text-center text-sm text-medium-emphasis">
          No proxies match this search.
        </p>
      );
    }
    return (
      <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
          <ProxyIcon className="h-7 w-7" />
        </div>
        <h4 className="mt-5 text-lg font-semibold text-high-emphasis">No proxies yet</h4>
        <p className="mt-2 max-w-md text-sm text-muted-foreground">
          Create your first proxy configuration to keep client traffic stable while upstream
          services stay behind a managed route.
        </p>
        <Button className="mt-6 gap-2" size="sm" onClick={() => navigate(scoped("proxy/new"))}>
          <Plus className="h-4 w-4" />
          Add proxy
        </Button>
      </div>
    );
  }

  return (
    <>
      <Table className="min-w-[920px] border-separate border-spacing-y-4">
        <TableHeader className="[&_tr]:border-0">
          <TableRow className="border-0">
            <TableHead className={HEAD_CLASS}>
              <div className="font-bold text-medium-emphasis">Name</div>
            </TableHead>
            <TableHead className={HEAD_CLASS}>
              <div className="font-bold text-medium-emphasis">Client route</div>
            </TableHead>
            <TableHead className={cn("w-[220px]", HEAD_CLASS)}>
              <div className="font-bold text-medium-emphasis">Methods</div>
            </TableHead>
            <TableHead className={HEAD_CLASS}>
              <div className="font-bold text-medium-emphasis">Upstream</div>
            </TableHead>
            <TableHead className={HEAD_CLASS}>
              <div className="font-bold text-medium-emphasis">Traffic</div>
            </TableHead>
            <TableHead className={cn("w-[120px]", HEAD_CLASS)}>
              <div className="font-bold text-medium-emphasis">Active</div>
            </TableHead>
            <TableHead className={cn("w-[80px]", HEAD_CLASS)}>
              <span className="sr-only">Row actions</span>
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
              <TableCell className={cn("rounded-l-lg border-l", CELL_CLASS)}>
                <div className="w-[200px] truncate font-semibold md:w-[240px]">{proxy.name}</div>
                {proxy.hasUnboundSecrets ? (
                  <span
                    className="mt-1 inline-flex items-center gap-1 rounded border border-amber-300/60 bg-amber-50 px-1.5 py-0.5 text-[11px] font-medium text-amber-800 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-300"
                    title="A secret this proxy uses is not linked yet. Open the proxy to see which."
                  >
                    <AlertTriangle aria-hidden="true" className="h-3 w-3" />
                    Check secrets
                  </span>
                ) : null}
              </TableCell>
              <TableCell className={CELL_CLASS}>
                <p className="w-[260px] truncate font-mono text-sm text-muted-foreground">
                  {getProxyClientUrl(selectedProject, proxy.slug)}
                </p>
              </TableCell>
              <TableCell className={cn("w-[220px]", CELL_CLASS)}>
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
              <TableCell className={CELL_CLASS}>
                <p
                  className="max-w-[280px] truncate text-sm text-muted-foreground"
                  title={proxy.upstreamUrl || proxy.upstreamMasked}
                >
                  {proxy.upstreamUrl || proxy.upstreamMasked}
                </p>
              </TableCell>
              <TableCell className={CELL_CLASS}>
                <span className="whitespace-nowrap text-sm text-muted-foreground">
                  {proxy.calls24h.toLocaleString()} recent calls
                </span>
              </TableCell>
              <TableCell className={cn("w-[120px]", CELL_CLASS)}>
                <div
                  className="flex items-center justify-start"
                  onClick={(event) => event.stopPropagation()}
                >
                  <Switch
                    aria-label={`${proxy.name} enabled`}
                    checked={proxy.enabled}
                    disabled={toggleProxy.isPending}
                    onCheckedChange={(checked) => handleToggle(proxy, checked)}
                  />
                </div>
              </TableCell>

              <TableCell className={cn("w-[80px] rounded-r-lg border-r", CELL_CLASS)}>
                <div
                  className="flex items-center justify-end"
                  onClick={(event) => event.stopPropagation()}
                >
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
                        className="cursor-pointer"
                        disabled={proxyExport.isPending}
                        onClick={() => proxyExport.handleExport(proxy)}
                      >
                        <Download className="mr-2 h-4 w-4" />
                        <span>Export</span>
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
