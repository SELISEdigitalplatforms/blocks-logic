"use client";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useScheduleFilterQueryParams } from "../../hooks/use-schedule-filter-query-params";

type ScheduleFilter = { search: string };

export const ScheduleFilterToolBar = () => {
  const { queryParams, setQueryParams } = useScheduleFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((params) => ({
      ...params,
      [key]: value,
      page: 0,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar<ScheduleFilter>
      filters={[{ key: "search", type: "SearchInput", label: "" }]}
      values={{ search: queryParams.search }}
      defaultValues={{ search: "" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
};
