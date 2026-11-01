"use client";
import { useMemo } from "react";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useScheduleExecutionQueryParams } from "../../hooks/use-schedule-execution-query-params";
import { DateRangeValue, fromDateRangeValue, toDateRangeValue } from "../../utils/execution-date-range";
import { useT, tStatic } from "@seliseblocks/genesis-os";

type ScheduleExecutionFilter = { outcome: string; firedDate: DateRangeValue };

const OUTCOME_OPTIONS = [
  { get label() { return tStatic("logic.scheduler", "common:ALL"); }, value: "all" },
  { get label() { return tStatic("logic.scheduler", "SCHEDULE_EXECUTION_FILTER_TOOLBAR.SUCCEEDED"); }, value: "Succeeded" },
  { get label() { return tStatic("logic.scheduler", "SCHEDULE_EXECUTION_FILTER_TOOLBAR.FAILED"); }, value: "Failed" },
];

/** Mirrors `WorkflowFilterToolBar`: any change resets to page 0; Reset clears every param. */
export const ScheduleExecutionFilterToolbar = () => {
  const { t } = useT("logic.scheduler");
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
        { key: "outcome", type: "Radio", label: t("SCHEDULE_EXECUTION_FILTER_TOOLBAR.OUTCOME"), props: { options: OUTCOME_OPTIONS } },
        { key: "firedDate", type: "DateRange", label: t("SCHEDULE_EXECUTION_FILTER_TOOLBAR.FIRED_DATE") },
      ]}
      values={{ outcome: queryParams.outcome, firedDate }}
      defaultValues={{ outcome: "all", firedDate: null }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
};
