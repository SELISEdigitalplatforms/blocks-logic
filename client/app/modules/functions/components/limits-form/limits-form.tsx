import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import { useGetLimitsOptions } from "../../hooks/use-functions";
import {
  CPU_MILLICORE_OPTIONS,
  DEFAULT_LIMITS_OPTIONS,
  HARD_CAPS,
  MEMORY_MB_OPTIONS,
  TIMEOUT_SECONDS_OPTIONS,
} from "../../constants/limits.constant";
import { IFunctionLimits } from "../../types/function.types";

type LimitsFormProps = {
  value: IFunctionLimits;
  onChange: (value: IFunctionLimits) => void;
};

/**
 * Steps, not free numbers: the design offers the values the sandbox honours and marks the ceiling.
 * A value already stored outside the list (an older function, or a ceiling that has since moved) is
 * kept as an extra option so opening this tab never silently rewrites it.
 */
const buildOptions = (steps: readonly number[], ceiling: number, current: number, suffix: string) => {
  const values = Array.from(new Set([...steps.filter((step) => step <= ceiling), current])).sort(
    (a, b) => a - b,
  );
  return values.map((option) => ({
    value: String(option),
    label: `${option}${suffix}${option === ceiling ? " — max" : ""}`,
  }));
};

export const LimitsForm = ({ value, onChange }: LimitsFormProps) => {
  const { data: options } = useGetLimitsOptions();
  const ceilings = options ?? DEFAULT_LIMITS_OPTIONS;

  const patch = (partial: Partial<IFunctionLimits>) => onChange({ ...value, ...partial });

  const selects = [
    {
      key: "memoryMb",
      label: "Memory",
      value: String(value.memoryMb),
      options: buildOptions(MEMORY_MB_OPTIONS, ceilings.ceilingMemoryMb, value.memoryMb, " MB"),
      onValueChange: (next: string) => patch({ memoryMb: Number(next) }),
    },
    {
      key: "cpuMillicores",
      label: "CPU",
      value: String(value.cpuMillicores),
      options: buildOptions(
        CPU_MILLICORE_OPTIONS,
        ceilings.ceilingCpuMillicores,
        value.cpuMillicores,
        "m",
      ),
      onValueChange: (next: string) => patch({ cpuMillicores: Number(next) }),
    },
    {
      key: "timeoutSeconds",
      label: "Timeout",
      value: String(value.timeoutSeconds),
      options: buildOptions(
        TIMEOUT_SECONDS_OPTIONS,
        ceilings.ceilingTimeoutSeconds,
        value.timeoutSeconds,
        " s",
      ),
      onValueChange: (next: string) => patch({ timeoutSeconds: Number(next) }),
    },
  ];

  const isConcurrencyValid =
    value.concurrency >= ceilings.minConcurrency && value.concurrency <= ceilings.maxConcurrency;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {selects.map((select) => (
          <div key={select.key} className="flex min-w-0 flex-col gap-1.5">
            <Label className="text-xs font-semibold">{select.label}</Label>
            <Select value={select.value} onValueChange={select.onValueChange}>
              <SelectTrigger className="h-9 text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {select.options.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        ))}

        <div className="flex min-w-0 flex-col gap-1.5">
          <Label className="text-xs font-semibold" htmlFor="fn-concurrency">
            Concurrent runs
          </Label>
          <Input
            id="fn-concurrency"
            type="number"
            className="h-9"
            min={ceilings.minConcurrency}
            max={ceilings.maxConcurrency}
            value={value.concurrency}
            onChange={(e) => patch({ concurrency: Number(e.target.value) })}
          />
          {isConcurrencyValid ? (
            <p className="text-xs text-medium-emphasis">
              Extra runs wait in the queue; nothing is rejected.
            </p>
          ) : (
            <p className="text-xs text-error">
              Choose between {ceilings.minConcurrency} and {ceilings.maxConcurrency}.
            </p>
          )}
        </div>
      </div>

      <p className="text-xs text-medium-emphasis">
        Enforced per invocation by the sandbox. Ceiling is {ceilings.ceilingMemoryMb} MB and{" "}
        {ceilings.ceilingCpuMillicores} millicores.
      </p>

      <div className="flex flex-wrap gap-2 border-t pt-3">
        {HARD_CAPS.map((cap) => (
          <span
            key={cap.label}
            className="flex items-center gap-1.5 rounded-md bg-surface-app px-2.5 py-1 text-xs text-medium-emphasis"
          >
            {cap.label} <b className="font-semibold text-foreground">{cap.value}</b>
          </span>
        ))}
      </div>

      {ceilings.showRateLimits && (
        <div className="grid gap-3 border-t pt-3 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label className="text-xs font-semibold">Requests / minute</Label>
            <Input
              type="number"
              className="h-9"
              min={0}
              placeholder="Unlimited"
              value={value.requestsPerMinute ?? ""}
              onChange={(e) =>
                patch({ requestsPerMinute: e.target.value ? Number(e.target.value) : null })
              }
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label className="text-xs font-semibold">Requests / day</Label>
            <Input
              type="number"
              className="h-9"
              min={0}
              placeholder="Unlimited"
              value={value.requestsPerDay ?? ""}
              onChange={(e) =>
                patch({ requestsPerDay: e.target.value ? Number(e.target.value) : null })
              }
            />
          </div>
        </div>
      )}
    </div>
  );
};
