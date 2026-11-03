"use client";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useWorkflowFilterQueryParams } from "@blocks-workflow/hooks";
import { useT } from "@seliseblocks/genesis-os";

type WorkflowFilter = { search: string; isPublished: string };

export function WorkflowFilterToolBar() {
  const { t } = useT("logic.workflow");
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
          label: t("common:STATUS"),
          props: {
            options: [
              { label: t("common:ALL"), value: "all" },
              { label: t("WORKFLOW_FILTER_TOOLBAR.PUBLISHED"), value: "1" },
              { label: t("WORKFLOW_FILTER_TOOLBAR.UNPUBLISHED"), value: "0" },
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
