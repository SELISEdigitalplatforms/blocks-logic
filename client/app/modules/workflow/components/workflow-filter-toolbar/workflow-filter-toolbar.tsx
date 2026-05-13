"use client";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useWorkflowFilterQueryParams } from "@blocks-workflow/hooks";

type WorkflowFilter = { search: string; isActive: string };

export function WorkflowFilterToolBar() {
  const { queryParams, setQueryParams } = useWorkflowFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((params) => ({
      ...params,
      [key]: value,
      page: 0,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar<WorkflowFilter>
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
