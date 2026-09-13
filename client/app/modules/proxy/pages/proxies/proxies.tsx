import { useState } from "react";
import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { PROXY_PAGE_SIZE, PROXY_PAGE_SIZE_OPTIONS } from "../../constants";
import { useGetProxies } from "../../hooks";
import { ProxyList } from "../../components/proxy-list";
import { VariablesButton } from "../../components/variables-button";

export const Proxies = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState<number>(PROXY_PAGE_SIZE);
  const { data, isLoading, isFetching } = useGetProxies({ pageNumber: page, pageSize });
  const proxies = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const shouldShowAddProxyButton = !isLoading && totalCount > 0;
  const showPagination = !isLoading && totalCount > pageSize;

  const handlePageSizeChange = (size: number) => {
    setPageSize(size);
    setPage(0);
  };

  const handleProxyDeleted = () => {
    if (page > 0 && proxies.length === 1) setPage(page - 1);
  };

  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Proxy</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Your client calls Blocks, Blocks adds the key and calls the third party. The vendor URL
            and secret never reach the browser.
          </p>
        </div>
        {shouldShowAddProxyButton && (
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <VariablesButton className="hidden" />
            <Button className="gap-2" onClick={() => navigate(scoped("proxy/new"))}>
              <Plus className="h-4 w-4" />
              Add proxy
            </Button>
          </div>
        )}
      </div>
      <Card>
        <CardContent>
          <div className={isFetching && !isLoading ? "opacity-60 transition-opacity" : undefined}>
            <ProxyList
              proxies={proxies}
              isLoading={isLoading}
              onProxyDeleted={handleProxyDeleted}
            />
          </div>
          {showPagination ? (
            <div className="mt-5 flex justify-end">
              <Pagination
                totalCount={totalCount}
                page={page}
                pageSize={pageSize}
                pageSizeOptions={[...PROXY_PAGE_SIZE_OPTIONS]}
                onChange={setPage}
                onPageSizeChange={handlePageSizeChange}
              />
            </div>
          ) : null}
        </CardContent>
      </Card>
    </section>
  );
};
