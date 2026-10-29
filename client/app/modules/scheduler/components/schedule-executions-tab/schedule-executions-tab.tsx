"use client";
import { useMemo, useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { PAGE_SIZE_OPTIONS, Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetScheduleExecutions } from "../../hooks/use-schedule-api";
import { useScheduleExecutionQueryParams } from "../../hooks/use-schedule-execution-query-params";
import { IGetScheduleExecutionsResponse, ISchedule } from "../../types/schedule.service.type";
import { isExecutionsFiltered, toExecutionsPayload } from "../../utils/execution-payload";
import { browserTimeZone } from "../../utils/timezone";
import { ScheduleExecutionSheet } from "./execution-sheet/schedule-execution-sheet";
import { ExecutionListError, NoExecutionsYet, NoMatchingExecutions, ScheduleGone } from "./execution-list-states";
import { ScheduleExecutionFilterToolbar } from "./schedule-execution-filter-toolbar";
import { ScheduleExecutionList } from "./schedule-execution-list";

interface ScheduleExecutionsTabProps {
  schedule: ISchedule;
  /** Only the active tab requests the list. */
  active: boolean;
}

type ListState = "list" | "empty" | "no-match" | "error" | "gone";

/** Which body the tab shows; loading is part of "list" (skeleton rows), as on /workflow. */
export function listStateOf(
  data: IGetScheduleExecutionsResponse | undefined,
  isError: boolean,
  isLoading: boolean,
  isFiltered: boolean,
): ListState {
  if (isLoading) return "list";
  if (data?.errors?.not_found) return "gone";
  if (isError || (data && !data.isSuccess)) return "error";
  if (data && data.totalCount === 0 && !data.data?.length) return isFiltered ? "no-match" : "empty";
  return "list";
}

export const ScheduleExecutionsTab = ({ schedule, active }: Readonly<ScheduleExecutionsTabProps>) => {
  const tz = schedule.timeZone ?? "UTC";
  const { queryParams, setQueryParams } = useScheduleExecutionQueryParams();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const payload = useMemo(() => toExecutionsPayload(schedule.itemId, queryParams, tz), [schedule.itemId, queryParams, tz]);
  const { data, isLoading, isFetching, isError, refetch } = useGetScheduleExecutions(payload, {
    enabled: active && !!schedule.itemId,
  });

  const isListLoading = isLoading || isFetching;
  const isFiltered = isExecutionsFiltered(payload);
  const state = listStateOf(data, isError, isListLoading, isFiltered);
  const showDatesNote = payload.startDate !== null && tz !== browserTimeZone();

  return (
    <Card>
      {state !== "empty" && state !== "gone" && (
        <CardHeader className="mb-0 space-y-2">
          <ScheduleExecutionFilterToolbar />
          {showDatesNote && <p className="text-xs text-muted-foreground">Dates are in {tz}</p>}
        </CardHeader>
      )}
      <CardContent>
        {state === "empty" && <NoExecutionsYet schedule={schedule} tz={tz} />}
        {state === "no-match" && <NoMatchingExecutions />}
        {state === "error" && <ExecutionListError onRetry={() => void refetch()} />}
        {state === "gone" && <ScheduleGone />}
        {state === "list" && (
          <ScheduleExecutionList
            executions={data?.data ?? []}
            isLoading={isListLoading}
            tz={tz}
            selectedId={selectedId}
            onSelect={setSelectedId}
          />
        )}

        {state === "list" && !!data?.totalCount && (
          <div className="mt-5 flex justify-end">
            <Pagination
              totalCount={data.totalCount}
              page={queryParams.page}
              pageSize={queryParams.pageSize}
              pageSizeOptions={PAGE_SIZE_OPTIONS}
              onChange={(page) => setQueryParams((params) => ({ ...params, page }))}
              onPageSizeChange={(pageSize) => setQueryParams((params) => ({ ...params, pageSize, page: 0 }))}
            />
          </div>
        )}
      </CardContent>
      <ScheduleExecutionSheet
        scheduleId={schedule.itemId}
        executionId={selectedId}
        tz={tz}
        currentName={schedule.name}
        onClose={() => setSelectedId(null)}
      />
    </Card>
  );
};
