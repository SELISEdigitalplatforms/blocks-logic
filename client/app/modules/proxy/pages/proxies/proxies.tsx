import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { PROXY_PAGE_SIZE_OPTIONS } from "../../constants";
import { useGetProxies, useProxyFilterQueryParams } from "../../hooks";
import { ProxyList } from "../../components/proxy-list";
import { ProxyFilterToolBar } from "../../components/proxy-filter-toolbar";
import { VariablesButton } from "../../components/variables-button";

export const Proxies = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { queryParams, setQueryParams } = useProxyFilterQueryParams();

  const { data, isLoading, isFetching } = useGetProxies({
    searchKey: queryParams.search,
    isActive: queryParams.isActive === "all" ? undefined : queryParams.isActive === "1",
    pageNumber: queryParams.page,
    pageSize: queryParams.pageSize,
  });

  const proxies = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const isListLoading = isLoading || isFetching;
  const isFiltered = !!queryParams.search || queryParams.isActive !== "all";
  const isEmpty = !isListLoading && totalCount === 0 && !isFiltered;

  const handleProxyDeleted = () => {
    if (queryParams.page > 0 && proxies.length === 1) {
      setQueryParams((params) => ({ ...params, page: params.page - 1 }));
    }
  };

  return (
    <section className="flex flex-col gap-6 p-4">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Proxy</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Your client calls Blocks, Blocks adds the key and calls the third party. The vendor URL
          and secret never reach the browser.
        </p>
      </div>
      <Card>
        {!isEmpty && (
          <CardHeader className="mb-0 flex flex-row items-center justify-between">
            <ProxyFilterToolBar />
            <div className="flex items-center gap-1">
              <VariablesButton className="hidden" />
              <Button
                size="sm"
                variant="ghost"
                className="text-primary hover:text-primary"
                onClick={() => navigate(scoped("proxy/new"))}
              >
                <Plus className="h-4 w-4" />
                <span className="sr-only sm:not-sr-only sm:ml-2.5">Add proxy</span>
              </Button>
            </div>
          </CardHeader>
        )}
        <CardContent>
          <div className={isFetching && !isLoading ? "opacity-60 transition-opacity" : undefined}>
            <ProxyList
              proxies={proxies}
              isLoading={isListLoading}
              onProxyDeleted={handleProxyDeleted}
              isFiltered={isFiltered}
            />
          </div>
          {totalCount > queryParams.pageSize && (
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
    </section>
  );
};
