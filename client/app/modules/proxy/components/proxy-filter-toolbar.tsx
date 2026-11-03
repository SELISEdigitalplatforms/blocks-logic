"use client";
import { FilterToolbar } from "@/components/filter-toolbar";
import { useProxyFilterQueryParams } from "../hooks";
import { useT } from "@seliseblocks/genesis-os";

type ProxyFilter = { search: string; isActive: string };

export function ProxyFilterToolBar() {
  const { t } = useT("logic.proxy.components");
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
          label: t("common:STATUS"),
          props: {
            options: [
              { label: t("common:ALL"), value: "all" },
              { label: t("PROXY_FILTER_TOOLBAR.ACTIVE"), value: "1" },
              { label: t("PROXY_FILTER_TOOLBAR.INACTIVE"), value: "0" },
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
