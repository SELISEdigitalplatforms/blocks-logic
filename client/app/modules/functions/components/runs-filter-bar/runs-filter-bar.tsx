import { RefreshCw, Search } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
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

/** Status chips, per FEATURES-AND-UI §4.8. "" is All; Running covers every non-terminal status. */
export const RUN_STATUS_FILTERS = [
  { label: "All", value: "" },
  { label: "Succeeded", value: "Succeeded" },
  { label: "Failed", value: "Failed" },
  { label: "Timed out", value: "TimedOut" },
  { label: "Running", value: "Running" },
];

const TRIGGER_FILTERS = [
  { label: "Any trigger", value: "" },
  { label: "HTTP", value: "Http" },
  { label: "Workflow", value: "Workflow" },
  { label: "Test", value: "Test" },
  { label: "Replay", value: "Replay" },
];

const RANGE_FILTERS = [
  { label: "Last 24 hours", value: "24h" },
  { label: "Last 7 days", value: "7d" },
  { label: "Last 30 days", value: "30d" },
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

export const RunsFilterBar = ({ value, onChange, hasActiveRun }: RunsFilterBarProps) => (
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
      <div className="relative min-w-[190px] flex-1">
        <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-medium-emphasis" />
        <Input
          aria-label="Search by run id"
          placeholder="Search by run id"
          className="h-9 pl-8 font-mono text-xs"
          value={value.search}
          onChange={(e) => onChange({ search: e.target.value })}
        />
      </div>

      <Select value={value.invokedBy || ""} onValueChange={(invokedBy) => onChange({ invokedBy })}>
        <SelectTrigger className="h-9 w-[150px]" aria-label="Trigger">
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
        <SelectTrigger className="h-9 w-[150px]" aria-label="Date range">
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
          Auto-refresh
        </Label>
      </div>
    </div>
  </div>
);
