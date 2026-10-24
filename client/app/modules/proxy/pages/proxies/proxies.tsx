import { useState } from "react";
import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { FileUp, Loader2, Plus, RotateCcw } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { PROXY_PAGE_SIZE_OPTIONS } from "../../constants";
import { useGetProxies, useProxyFilterQueryParams } from "../../hooks";
import { ProxyList } from "../../components/proxy-list";
import { ProxyFilterToolBar } from "../../components/proxy-filter-toolbar";
import { VariablesButton } from "../../components/variables-button";
import { ProxyImportDialog } from "../../components/proxy-import-dialog";

export const Proxies = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { queryParams, setQueryParams } = useProxyFilterQueryParams();
  const [isImportOpen, setIsImportOpen] = useState(false);

  const { data, isLoading, isFetching, isError, refetch } = useGetProxies({
    search: queryParams.search,
    isActive: queryParams.isActive === "all" ? undefined : queryParams.isActive === "1",
    pageNumber: queryParams.page,
    pageSize: queryParams.pageSize,
  });

  const proxies = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  // Skeletons only on the first load: a background refetch (a filter change, a return to the tab)
  // keeps the rows on screen instead of flashing the whole table away.
  const isFiltered = !!queryParams.search || queryParams.isActive !== "all";
  // Same rule Functions uses: while the list is loading, and while the project has no proxies at
  // all, the header button stays out of the way — the empty state carries the only call to
  // action, so the page never shows two. A filter that matches nothing is not an empty project,
  // and its "no matches" line has no button of its own, so the header keeps one.
  const showCreateButton = !isLoading && (totalCount > 0 || isFiltered);

  const handleProxyDeleted = () => {
    if (queryParams.page > 0 && proxies.length === 1) {
      setQueryParams((params) => ({ ...params, page: params.page - 1 }));
    }
  };

  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-2xl font-bold tracking-tight">Proxy</h1>
          <p className="mt-1 text-sm text-medium-emphasis">
            Your client calls Blocks, Blocks adds the key and calls the third party. Your client
            never sees the vendor URL or the key. Store keys as secrets.
          </p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <VariablesButton className="hidden" />
          {/* Import is always offered: an empty environment is exactly where a file is brought in. */}
          <Button
            size="sm"
            variant="outline"
            className="gap-1.5"
            onClick={() => setIsImportOpen(true)}
          >
            <FileUp className="h-4 w-4" />
            Import
          </Button>
          {showCreateButton && (
            <Button size="sm" className="gap-1.5" onClick={() => navigate(scoped("proxy/new"))}>
              <Plus className="h-4 w-4" />
              Add proxy
            </Button>
          )}
        </div>
      </div>

      {isError ? (
        <Card>
          <CardContent className="flex flex-col items-center gap-3 py-14 text-center">
            <p className="text-sm font-semibold">Proxies could not be loaded</p>
            <p className="max-w-md text-xs text-medium-emphasis">
              The request failed before the list came back. Nothing has changed — try again.
            </p>
            <Button variant="outline" size="sm" className="gap-1.5" onClick={() => void refetch()}>
              <RotateCcw className="h-3.5 w-3.5" />
              Retry
            </Button>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardHeader className="mb-4 flex flex-row items-center gap-3">
            <ProxyFilterToolBar />
            {isFetching && !isLoading && (
              <Loader2 className="h-4 w-4 shrink-0 animate-spin text-medium-emphasis" />
            )}
          </CardHeader>
          <CardContent>
            <ProxyList
              proxies={proxies}
              isLoading={isLoading}
              onProxyDeleted={handleProxyDeleted}
              isFiltered={isFiltered}
              rowCount={queryParams.pageSize}
            />

            {/* The pager hides itself when there is one page and no page size worth choosing. */}
            {totalCount > 0 && (
              <div className="mt-5 flex justify-end">
                <Pagination
                  totalCount={totalCount}
                  page={queryParams.page}
                  pageSize={queryParams.pageSize}
                  pageSizeOptions={[...PROXY_PAGE_SIZE_OPTIONS]}
                  onChange={(page) => setQueryParams((params) => ({ ...params, page }))}
                  onPageSizeChange={(pageSize) =>
                    setQueryParams((params) => ({ ...params, pageSize, page: 0 }))
                  }
                />
              </div>
            )}
          </CardContent>
        </Card>
      )}

      <ProxyImportDialog
        open={isImportOpen}
        onOpenChange={setIsImportOpen}
        onImported={(proxyId) => navigate(scoped(`proxy/${proxyId}`))}
      />
    </section>
  );
};
