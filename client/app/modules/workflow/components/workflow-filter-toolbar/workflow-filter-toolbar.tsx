"use client";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useWorkflowFilterQueryParams } from "@blocks-workflow/hooks";

type WorkflowFilter = { search: string; isPublished: string };

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
          key: "isPublished",
          type: "Radio",
          label: "Status",
          props: {
            options: [
              { label: "All", value: "all" },
              { label: "Published", value: "1" },
              { label: "Unpublished", value: "0" },
            ],
          },
        },
      ]}
      values={{ search: queryParams.search, isPublished: queryParams.isPublished }}
      defaultValues={{ search: "", isPublished: "all" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
}
