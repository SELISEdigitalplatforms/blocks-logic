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

/**
 * Segmented status filter, per FEATURES-AND-UI §4.1 — "" is All. `Paused` is not in the spec's
 * list but is a real server status (FunctionEnums.FunctionStatus), so it gets a chip: without one
 * a paused function is reachable only through All and the control implies it cannot exist.
 */
const STATUS_OPTIONS = [
  { label: "All", value: "" },
  { label: "Live", value: "Live" },
  { label: "Draft", value: "Draft" },
  { label: "Paused", value: "Paused" },
];

/**
 * Sort options are the two the API can serve from the functions collection itself; "last run"
 * lives in the run-stats collection and would need a lookup to sort by.
 */
const SORT_OPTIONS = [
  { label: "Recently updated", value: "Updated" },
  { label: "Name", value: "Name" },
];

export const FunctionsFilterToolBar = () => {
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
          aria-label="Search functions"
          placeholder="Search by name"
          className="h-8 w-full"
          value={queryParams.search}
          onChange={(search) => patch({ search })}
        />
      </div>

      <div className="flex overflow-hidden rounded-md border" role="group" aria-label="Status">
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
      </div>

      <Select
        value={queryParams.sort || "Updated"}
        onValueChange={(sort) => patch({ sort: sort === "Updated" ? "" : sort })}
      >
        <SelectTrigger className="h-9 w-[170px] shrink-0" aria-label="Sort">
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
