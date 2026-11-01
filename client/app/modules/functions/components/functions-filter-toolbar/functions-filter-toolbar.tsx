"use client";
import { SearchInput } from "@/components/filter-toolbar/search-input/search-input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { cn } from "@/lib/utils";
import { useFunctionsFilterQueryParams } from "../../hooks/use-functions-filter-query-params";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

/**
 * Segmented status filter, per FEATURES-AND-UI §4.1 — "" is All. `Paused` is not in the spec's
 * list but is a real server status (FunctionEnums.FunctionStatus), so it gets a chip: without one
 * a paused function is reachable only through All and the control implies it cannot exist.
 */
const STATUS_OPTIONS = [
  {
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "common:ALL");
    },
    value: "",
  },
  {
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "FUNCTIONS_FILTER_TOOLBAR.LIVE");
    },
    value: "Live",
  },
  {
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "FUNCTIONS_FILTER_TOOLBAR.DRAFT");
    },
    value: "Draft",
  },
  {
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "FUNCTIONS_FILTER_TOOLBAR.PAUSED");
    },
    value: "Paused",
  },
];

/**
 * Sort options are the two the API can serve from the functions collection itself; "last run"
 * lives in the run-stats collection and would need a lookup to sort by.
 */
const SORT_OPTIONS = [
  {
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "FUNCTIONS_FILTER_TOOLBAR.RECENTLY_UPDATED");
    },
    value: "Updated",
  },
  {
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "common:NAME");
    },
    value: "Name",
  },
];

export const FunctionsFilterToolBar = () => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  const { queryParams, setQueryParams } = useFunctionsFilterQueryParams();

  const patch = (partial: Record<string, unknown>) =>
    setQueryParams((params) => ({ ...params, ...partial, page: 0 }));

  return (
    <div className="flex flex-1 flex-wrap items-center gap-3">
      {/*
        The shared SearchInput debounces at 300 ms. Writing straight to the nuqs param on every
        keystroke would put a character-by-character request on the wire (and a history entry
        behind every one of them), because `search` is part of the functions query key.
      */}
      <div className="min-w-[220px] flex-1">
        <SearchInput
          aria-label={t("FUNCTIONS_FILTER_TOOLBAR.SEARCH_FUNCTIONS")}
          placeholder={t("FUNCTIONS_FILTER_TOOLBAR.SEARCH_BY_NAME")}
          className="h-8 w-full"
          value={queryParams.search}
          onChange={(search) => patch({ search })}
        />
      </div>

      <fieldset
        className="m-0 flex min-w-0 overflow-hidden rounded-md border p-0"
        aria-label={t("common:STATUS")}
      >
        {STATUS_OPTIONS.map((option) => (
          <button
            key={option.label}
            type="button"
            aria-pressed={queryParams.status === option.value}
            className={cn(
              "px-3 py-1.5 text-xs font-semibold transition-colors",
              "focus-visible:relative focus-visible:z-10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              queryParams.status === option.value
                ? "bg-primary text-primary-foreground"
                : "bg-background text-medium-emphasis hover:bg-surface-app",
            )}
            onClick={() => patch({ status: option.value })}
          >
            {option.label}
          </button>
        ))}
      </fieldset>

      <Select
        value={queryParams.sort || "Updated"}
        onValueChange={(sort) => patch({ sort: sort === "Updated" ? "" : sort })}
      >
        <SelectTrigger
          className="h-9 w-[170px] shrink-0"
          aria-label={t("FUNCTIONS_FILTER_TOOLBAR.SORT")}
        >
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {SORT_OPTIONS.map((option) => (
            <SelectItem key={option.value} value={option.value}>
              {option.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
};
