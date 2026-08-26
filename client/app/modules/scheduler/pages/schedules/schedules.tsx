import { useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Button } from "@/components/ui-kits/button/button";
import { Plus } from "lucide-react";
import { ScheduleList } from "../../components/schedule-list";
import { ScheduleFilterToolBar } from "../../components/schedule-filter-toolbar";
import { useGetSchedules, useScheduleFilterQueryParams } from "../../hooks";

export const Schedules = () => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { queryParams, setQueryParams } = useScheduleFilterQueryParams();
  const { data, isLoading, isFetching } = useGetSchedules({
    searchKey: queryParams.search || "",
    pageNumber: Number(queryParams.page),
    pageSize: Number(queryParams.pageSize),
  });
  const schedules = data?.data || [];
  const isListLoading = isLoading || isFetching;
  const isEmpty = !isListLoading && schedules.length === 0;

  const handleCreateSchedule = () => {
    navigate(scoped("schedule/new"));
  };


  return (
    <section className="flex flex-col gap-6 p-4">
      <div className="flex min-h-10 items-center justify-between">
        <div className="flex items-center gap-3">
          <h3 className="flex items-center gap-2 text-2xl font-bold tracking-tight">Schedules</h3>
        </div>
      </div>
      <Card>
        {!isEmpty && (
          <CardHeader className="flex flex-row items-center justify-between mb-0">
            <ScheduleFilterToolBar />
            <Button
              size="sm"
              variant="ghost"
              className="text-primary hover:text-primary"
              onClick={handleCreateSchedule}
            >
              <Plus className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only sm:ml-2.5">Add Schedule</span>
            </Button>
          </CardHeader>
        )}
        <CardContent>
          <ScheduleList
            schedules={schedules}
            isLoading={isListLoading}
            onCreateSchedule={handleCreateSchedule}
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
  );
};

