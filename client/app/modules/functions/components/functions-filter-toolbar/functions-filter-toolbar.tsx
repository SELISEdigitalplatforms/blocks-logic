"use client";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useFunctionsFilterQueryParams } from "../../hooks/use-functions-filter-query-params";

type FunctionsFilter = { search: string; status: string };

const STATUS_OPTIONS = [
  { label: "Draft", value: "Draft" },
  { label: "Live", value: "Live" },
  { label: "Paused", value: "Paused" },
];

export const FunctionsFilterToolBar = () => {
  const { queryParams, setQueryParams } = useFunctionsFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((params) => ({
      ...params,
      [key]: value,
      page: 0,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar<FunctionsFilter>
      filters={[
        { key: "search", type: "SearchInput", label: "" },
        { key: "status", type: "Radio", label: "Status", props: { options: STATUS_OPTIONS } },
      ]}
      values={{ search: queryParams.search, status: queryParams.status }}
      defaultValues={{ search: "", status: "" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
};
