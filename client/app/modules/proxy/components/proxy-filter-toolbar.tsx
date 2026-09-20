"use client";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useProxyFilterQueryParams } from "../hooks";

type ProxyFilter = { search: string; isActive: string };

export function ProxyFilterToolBar() {
  const { queryParams, setQueryParams } = useProxyFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((params) => ({ ...params, [key]: value, page: 0 }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar<ProxyFilter>
      filters={[
        { key: "search", type: "SearchInput", label: "" },
        {
          key: "isActive",
          type: "Radio",
          label: "Status",
          props: {
            options: [
              { label: "All", value: "all" },
              { label: "Active", value: "1" },
              { label: "Inactive", value: "0" },
            ],
          },
        },
      ]}
      values={{ search: queryParams.search, isActive: queryParams.isActive }}
      defaultValues={{ search: "", isActive: "all" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
}
