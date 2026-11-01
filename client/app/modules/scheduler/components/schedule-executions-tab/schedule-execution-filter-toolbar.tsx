"use client";
import { useMemo } from "react";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useScheduleExecutionQueryParams } from "../../hooks/use-schedule-execution-query-params";
import { DateRangeValue, fromDateRangeValue, toDateRangeValue } from "../../utils/execution-date-range";

type ScheduleExecutionFilter = { outcome: string; firedDate: DateRangeValue };

const OUTCOME_OPTIONS = [
  { label: "All", value: "all" },
  { label: "Succeeded", value: "Succeeded" },
  { label: "Failed", value: "Failed" },
];

/** Mirrors `WorkflowFilterToolBar`: any change resets to page 0; Reset clears every param. */
export const ScheduleExecutionFilterToolbar = () => {
  const { queryParams, setQueryParams } = useScheduleExecutionQueryParams();
  // Stable identity: the DateRange control resyncs its draft whenever its value object changes.
  const firedDate = useMemo(
    () => toDateRangeValue(queryParams.from, queryParams.to),
    [queryParams.from, queryParams.to],
  );

  const changeHandler = (key: string, value: unknown) => {
    const changed =
      key === "firedDate" ? fromDateRangeValue(value as DateRangeValue) : { [key]: value };
    setQueryParams((params) => ({ ...params, ...changed, page: 0 }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar<ScheduleExecutionFilter>
      filters={[
        { key: "outcome", type: "Radio", label: "Outcome", props: { options: OUTCOME_OPTIONS } },
        { key: "firedDate", type: "DateRange", label: "Fired date" },
      ]}
      values={{ outcome: queryParams.outcome, firedDate }}
      defaultValues={{ outcome: "all", firedDate: null }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
};
