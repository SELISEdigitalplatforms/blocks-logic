"use client";
import { Search } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { cn } from "@/lib/utils";
import { useFunctionsFilterQueryParams } from "../../hooks/use-functions-filter-query-params";

/** Segmented status filter, per FEATURES-AND-UI §4.1 — "" is All. */
const STATUS_OPTIONS = [
  { label: "All", value: "" },
  { label: "Live", value: "Live" },
  { label: "Draft", value: "Draft" },
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
    <div className="flex flex-wrap items-center gap-3">
      <div className="relative min-w-[200px] flex-1">
        <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-medium-emphasis" />
        <Input
          aria-label="Search functions"
          placeholder="Search by name"
          className="h-9 pl-8"
          value={queryParams.search}
          onChange={(e) => patch({ search: e.target.value })}
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
        <SelectTrigger className="h-9 w-[170px]" aria-label="Sort">
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
