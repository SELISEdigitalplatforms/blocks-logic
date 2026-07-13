"use client";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { WorkflowList } from "../../components/workflow-list";
import { AddWorkflow } from "../../components/add-workflow";
import { WorkflowFilterToolBar } from "../../components/workflow-filter-toolbar";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetWorkflows } from "@blocks-workflow/hooks/use-workflow-api";
import { useWorkflowFilterQueryParams } from "@blocks-workflow/hooks";

export const Workflows = () => {
  const { queryParams, setQueryParams } = useWorkflowFilterQueryParams();
  const { data, isLoading, isFetching } = useGetWorkflows({
    pageSize: Number(queryParams.pageSize),
    pageNumber: Number(queryParams.page),
    // projectKey: tenantId,
    search: queryParams.search || "",
    isPublished:
      queryParams.isPublished === "all"
        ? undefined
        : queryParams.isPublished === "1"
          ? true
          : false,
  });
  return (
    <>
      <section className="flex flex-col gap-6 p-4">
        <div className="flex min-h-10 items-center justify-between">
          <div className="flex items-center gap-3">
            <h3 className="flex items-center gap-2 text-2xl font-bold tracking-tight">
              Workflow
            </h3>
          </div>
        </div>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <WorkflowFilterToolBar />
            <AddWorkflow />
          </CardHeader>
          <CardContent className="mt-5">
            <WorkflowList
              workflow={data?.data || []}
              isLoading={isLoading || isFetching}
            />

            {!!data?.totalCount && (
              <div className="mt-5 flex justify-end">
                <Pagination
                  totalCount={data?.totalCount || 0}
                  page={queryParams.page}
                  pageSize={queryParams.pageSize}
                  pageSizeOptions={[5, 10]}
                  onChange={(page) =>
                    setQueryParams((params) => ({ ...params, page }))
                  }
                  onPageSizeChange={(pageSize) =>
                    setQueryParams((params) => ({
                      ...params,
                      pageSize,
                      page: 0,
                    }))
                  }
                />
              </div>
            )}
          </CardContent>
        </Card>
      </section>
    </>
  );
};
