import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Button } from "@/components/ui-kits/button/button";
import { Plus } from "lucide-react";
import { FunctionsTable } from "../../components/functions-table";
import { FunctionsFilterToolBar } from "../../components/functions-filter-toolbar";
import { FunctionCreateDialog } from "../../components/function-create-dialog";
import { useGetFunctions } from "../../hooks/use-functions";
import { useFunctionsFilterQueryParams } from "../../hooks/use-functions-filter-query-params";

export const FunctionsPage = () => {
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const { queryParams, setQueryParams } = useFunctionsFilterQueryParams();
  const { data, isLoading, isFetching } = useGetFunctions({
    searchKey: queryParams.search || undefined,
    status: queryParams.status || undefined,
    pageNumber: Number(queryParams.page),
    pageSize: Number(queryParams.pageSize),
  });
  const functions = data?.data || [];
  const isListLoading = isLoading || isFetching;
  const isEmpty = !isListLoading && functions.length === 0;

  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex min-h-10 items-center justify-between">
        <div className="flex items-center gap-3">
          <h3 className="flex items-center gap-2 text-2xl font-bold tracking-tight">Functions</h3>
        </div>
      </div>
      <Card>
        {!isEmpty && (
          <CardHeader className="mb-0 flex flex-row items-center justify-between">
            <FunctionsFilterToolBar />
            <Button
              size="sm"
              variant="ghost"
              className="text-primary hover:text-primary"
              onClick={() => setIsCreateOpen(true)}
            >
              <Plus className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only sm:ml-2.5">Add Function</span>
            </Button>
          </CardHeader>
        )}
        <CardContent>
          <FunctionsTable
            functions={functions}
            isLoading={isListLoading}
            onCreateFunction={() => setIsCreateOpen(true)}
          />

          {!!data?.totalCount && (
            <div className="mt-5 flex justify-end">
              <Pagination
                totalCount={data?.totalCount || 0}
                page={queryParams.page}
                pageSize={queryParams.pageSize}
                pageSizeOptions={[5, 10]}
                onChange={(page) => setQueryParams((params) => ({ ...params, page }))}
                onPageSizeChange={(pageSize) =>
                  setQueryParams((params) => ({ ...params, pageSize, page: 0 }))
                }
              />
            </div>
          )}
        </CardContent>
      </Card>

      <FunctionCreateDialog open={isCreateOpen} onOpenChange={setIsCreateOpen} />
    </section>
  );
};
