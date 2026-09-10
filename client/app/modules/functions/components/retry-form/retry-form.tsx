import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Label } from "@/components/ui-kits/label/label";
import {
  BACKOFF_DELAYS,
  BACKOFF_KIND_OPTIONS,
  RETRY_ATTEMPTS_OPTIONS,
} from "../../constants/limits.constant";
import { BackoffKind, IRetryPolicy } from "../../types/function.types";

type RetryFormProps = {
  value: IRetryPolicy;
  onChange: (value: IRetryPolicy) => void;
};

const summarise = ({ attempts, backoff }: IRetryPolicy) => {
  if (attempts <= 1) return "No retry — a failure is final and the run is kept for manual replay.";
  const cadence =
    backoff === "Exponential" ? "backing off 1 s, 5 s, 20 s" : "waiting 5 s between attempts";
  return `Up to ${attempts} attempts, ${cadence}. Every attempt carries the same idempotency key.`;
};

/**
 * One policy for the whole function. Attempts and backoff are the only two decisions in the design;
 * the concrete delays behind a backoff choice are canonical (`BACKOFF_DELAYS`) rather than typed in,
 * so what the UI promises and what the scheduler does cannot drift apart.
 */
export const RetryForm = ({ value, onChange }: RetryFormProps) => {
  const setAttempts = (attempts: number) => {
    const backoff: BackoffKind =
      attempts <= 1 ? "None" : value.backoff === "None" ? "Exponential" : value.backoff;
    onChange({ attempts, backoff, ...BACKOFF_DELAYS[backoff] });
  };

  const setBackoff = (backoff: BackoffKind) =>
    onChange({ ...value, backoff, ...BACKOFF_DELAYS[backoff] });

  const isRetrying = value.attempts > 1;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <Label className="text-xs font-semibold">Attempts</Label>
          <Select
            value={String(value.attempts)}
            onValueChange={(next) => setAttempts(Number(next))}
          >
            <SelectTrigger className="h-9 text-sm" aria-label="Attempts">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {RETRY_ATTEMPTS_OPTIONS.map((attempts) => (
                <SelectItem key={attempts} value={String(attempts)}>
                  {attempts === 1 ? "1 — no retry" : String(attempts)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label className="text-xs font-semibold">Backoff</Label>
          <Select
            value={isRetrying ? value.backoff : ""}
            disabled={!isRetrying}
            onValueChange={(next) => setBackoff(next as BackoffKind)}
          >
            <SelectTrigger className="h-9 text-sm" aria-label="Backoff">
              <SelectValue placeholder="Not used with a single attempt" />
            </SelectTrigger>
            <SelectContent>
              {BACKOFF_KIND_OPTIONS.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      <p className="text-xs text-medium-emphasis">{summarise(value)}</p>
    </div>
  );
};
