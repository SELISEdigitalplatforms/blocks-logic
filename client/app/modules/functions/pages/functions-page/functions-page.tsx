import { useState } from "react";
import { Loader2, Plus, RotateCcw } from "lucide-react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Button } from "@/components/ui-kits/button/button";
import { FunctionsTable } from "../../components/functions-table";
import { FunctionsFilterToolBar } from "../../components/functions-filter-toolbar";
import { FunctionCreateDialog } from "../../components/function-create-dialog";
import { useGetFunctions } from "../../hooks/use-functions";
import { useFunctionsFilterQueryParams } from "../../hooks/use-functions-filter-query-params";
import { FunctionSort } from "../../types/function.types";

export const FunctionsPage = () => {
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const { queryParams, setQueryParams } = useFunctionsFilterQueryParams();
  const { data, isLoading, isFetching, isError, refetch } = useGetFunctions({
    searchKey: queryParams.search || undefined,
    status: queryParams.status || undefined,
    sortBy: (queryParams.sort as FunctionSort) || undefined,
    pageNumber: Number(queryParams.page),
    pageSize: Number(queryParams.pageSize),
  });
  const functions = data?.data || [];
  // Skeletons only on the first load: a background refetch (a filter change, a return to the tab)
  // keeps the rows on screen instead of flashing the whole table away.
  const hasFilters = !!queryParams.search || !!queryParams.status;

  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <h1 className="text-2xl font-bold tracking-tight">Functions</h1>
          <p className="mt-1 text-sm text-medium-emphasis">
            Deploy code and call it over HTTP or from a workflow — each run in its own sandbox.
          </p>
        </div>
        <Button size="sm" className="shrink-0 gap-1.5" onClick={() => setIsCreateOpen(true)}>
          <Plus className="h-4 w-4" />
          New function
        </Button>
      </div>

      {isError ? (
        <Card>
          <CardContent className="flex flex-col items-center gap-3 py-14 text-center">
            <p className="text-sm font-semibold">Functions could not be loaded</p>
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
          <CardHeader className="mb-0 flex flex-row items-center gap-3">
            <FunctionsFilterToolBar />
            {isFetching && !isLoading && (
              <Loader2 className="h-4 w-4 shrink-0 animate-spin text-medium-emphasis" />
            )}
          </CardHeader>
          <CardContent>
            <FunctionsTable
              functions={functions}
              isLoading={isLoading}
              hasFilters={hasFilters}
              onCreateFunction={() => setIsCreateOpen(true)}
            />

            {!!data?.totalCount && (
              <div className="mt-5 flex justify-end">
                <Pagination
                  totalCount={data?.totalCount || 0}
                  page={queryParams.page}
                  pageSize={queryParams.pageSize}
                  pageSizeOptions={[5, 10, 20]}
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

      <FunctionCreateDialog open={isCreateOpen} onOpenChange={setIsCreateOpen} />
    </section>
  );
};
