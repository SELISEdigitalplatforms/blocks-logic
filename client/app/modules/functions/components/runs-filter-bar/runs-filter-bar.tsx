import { RefreshCw } from "lucide-react";
import { SearchInput } from "@/components/filter-toolbar/search-input/search-input";
import { Switch } from "@/components/ui-kits/switch/switch";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { cn } from "@/lib/utils";
import { useT, tStatic } from "@seliseblocks/genesis-os";

/** Status chips, per FEATURES-AND-UI §4.8. "" is All; Running covers every non-terminal status. */
export const RUN_STATUS_FILTERS = [
  {
    get label() {
      return tStatic("logic.functions.components", "common:ALL");
    },
    value: "",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.SUCCEEDED");
    },
    value: "Succeeded",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.FAILED");
    },
    value: "Failed",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.TIMED_OUT");
    },
    value: "TimedOut",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.RUNNING");
    },
    value: "Running",
  },
];

/** Radix Select cannot hold "" as an item value — it means "nothing selected" and shows a blank
 *  trigger — so "any" is the sentinel and is mapped back to "" for the query. */
const ANY_TRIGGER = "any";

const TRIGGER_FILTERS = [
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.ANY_TRIGGER");
    },
    value: ANY_TRIGGER,
  },
  { label: "HTTP", value: "Http" },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.WORKFLOW");
    },
    value: "Workflow",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.TEST");
    },
    value: "Test",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.REPLAY");
    },
    value: "Replay",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.EVENT");
    },
    value: "Event",
  },
];

const RANGE_FILTERS = [
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.LAST_24_HOURS");
    },
    value: "24h",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.LAST_7_DAYS");
    },
    value: "7d",
  },
  {
    get label() {
      return tStatic("logic.functions.components", "RUNS_FILTER_BAR.LAST_30_DAYS");
    },
    value: "30d",
  },
];

export type RunsFilterValue = {
  status: string;
  invokedBy: string;
  range: string;
  search: string;
  autoRefresh: boolean;
};

type RunsFilterBarProps = {
  value: RunsFilterValue;
  onChange: (partial: Partial<RunsFilterValue>) => void;
  /** True while a run on this page is still active — the auto-refresh hint. */
  hasActiveRun: boolean;
};

export const RunsFilterBar = ({ value, onChange, hasActiveRun }: RunsFilterBarProps) => {
  const { t } = useT("logic.functions.components");
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-1">
        {RUN_STATUS_FILTERS.map((filter) => (
          <button
            key={filter.label}
            type="button"
            aria-pressed={value.status === filter.value}
            className={cn(
              "rounded-md px-3 py-1.5 text-xs transition-colors",
              value.status === filter.value
                ? "bg-blocks-primary-50 font-semibold text-primary"
                : "font-medium text-medium-emphasis hover:bg-surface-app",
            )}
            onClick={() => onChange({ status: filter.value })}
          >
            {filter.label}
          </button>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        {/*
          Debounced (300 ms) by the shared control: `search` is part of the runs query key, so a raw
          onChange put one request — and one history entry — on the wire per character typed.
        */}
        <div className="min-w-[190px] flex-1">
          <SearchInput
            aria-label={t("RUNS_FILTER_BAR.SEARCH_BY_RUN_ID")}
            placeholder={t("RUNS_FILTER_BAR.SEARCH_BY_RUN_ID")}
            className="h-8 w-full font-mono text-xs"
            value={value.search}
            onChange={(search) => onChange({ search })}
          />
        </div>

        <Select
          value={value.invokedBy || ANY_TRIGGER}
          onValueChange={(invokedBy) =>
            onChange({ invokedBy: invokedBy === ANY_TRIGGER ? "" : invokedBy })
          }
        >
          <SelectTrigger className="h-9 w-[150px]" aria-label={t("RUNS_FILTER_BAR.TRIGGER")}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {TRIGGER_FILTERS.map((filter) => (
              <SelectItem key={filter.label} value={filter.value}>
                {filter.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select value={value.range} onValueChange={(range) => onChange({ range })}>
          <SelectTrigger className="h-9 w-[150px]" aria-label={t("RUNS_FILTER_BAR.DATE_RANGE")}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {RANGE_FILTERS.map((filter) => (
              <SelectItem key={filter.value} value={filter.value}>
                {filter.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <div className="flex items-center gap-2">
          <Switch
            id="runs-auto-refresh"
            checked={value.autoRefresh}
            onCheckedChange={(autoRefresh) => onChange({ autoRefresh })}
          />
          <Label
            htmlFor="runs-auto-refresh"
            className="flex items-center gap-1.5 text-xs text-medium-emphasis"
          >
            <RefreshCw
              className={cn("h-3.5 w-3.5", value.autoRefresh && hasActiveRun && "animate-spin")}
            />
            {t("RUNS_FILTER_BAR.AUTO_REFRESH")}
          </Label>
        </div>
      </div>
    </div>
  );
};
