import { ReactNode } from "react";
import { AlertTriangle } from "lucide-react";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Input } from "@/components/ui-kits/input/input";
import { Label } from "@/components/ui-kits/label/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ProxyBackoff, ProxyResilience } from "../types";
import {
  PROXY_PLATFORM_TIMEOUT_SECONDS,
  PROXY_RESILIENCE_LIMITS,
  patchResilience,
  startingBreaker,
  startingRetry,
  startingTimeoutSeconds,
} from "../utils";

/** Errors keyed the way `resilienceIssues` paths read: `timeoutSeconds`, `retry.attempts`, … */
export type ResilienceErrors = Record<string, string | undefined>;

type Props = {
  value: ProxyResilience | null;
  onChange: (next: ProxyResilience | null) => void;
  /** Disambiguates the input ids and the switch labels when several of these are on one page. */
  idPrefix: string;
  /** Spoken name of the thing being configured, for the switch labels: "this proxy", "endpoint 2". */
  subject: string;
  errors?: ResilienceErrors;
};

/**
 * A number that is cleared on screen is stored as 0, which is out of every range here and so reads
 * back as the message under the field. The alternative — NaN — would fail the schema's own parse and
 * replace that message with "Expected number".
 */
const numberValue = (value: number) => (value === 0 ? "" : String(value));
const toNumber = (raw: string) => (raw.trim() === "" ? 0 : Number.parseInt(raw, 10) || 0);

const BACKOFF_LABELS: Array<{ value: ProxyBackoff; label: string; hint: string }> = [
  { value: "exponential", label: "Back off", hint: "Double the wait each time." },
  { value: "fixed", label: "Fixed wait", hint: "The same wait before every attempt." },
  { value: "none", label: "Immediately", hint: "No wait between attempts." },
];

/**
 * One setting, with both of its states spelled out. The heading row carries the switch and the
 * sentence saying what happens right now — including when nothing is configured, which is a state
 * worth naming rather than a blank.
 */
const Section = ({
  title,
  on,
  onToggle,
  switchLabel,
  summary,
  children,
}: {
  title: string;
  on: boolean;
  onToggle: (on: boolean) => void;
  switchLabel: string;
  summary: ReactNode;
  children?: ReactNode;
}) => (
  <div className="rounded-lg border p-3">
    <div className="flex items-start justify-between gap-3">
      <div className="min-w-0">
        <p className="text-xs font-medium">{title}</p>
        <p className="text-[11px] text-muted-foreground">{summary}</p>
      </div>
      <Switch checked={on} aria-label={switchLabel} onCheckedChange={onToggle} />
    </div>
    {on && children ? <div className="mt-3 space-y-3">{children}</div> : null}
  </div>
);

const FieldError = ({ message }: { message?: string }) =>
  message ? <p className="text-[11px] text-destructive">{message}</p> : null;

/**
 * The timeout / retries / circuit breaker editor, shared by the proxy-wide card and the per-endpoint
 * override so the two cannot drift apart.
 *
 * Controlled and form-agnostic on purpose: the proxy holds its policy in one field and an endpoint
 * holds its own inside a route, and neither shape should leak into the other's editor.
 *
 * Nothing is pre-filled. A value only appears once a switch is turned on, because the console sends
 * every field back on save — a number that showed up merely by looking at this panel would then be
 * saved as if it had been chosen.
 */
export const ProxyResilienceFields = ({ value, onChange, idPrefix, subject, errors }: Props) => {
  const limits = PROXY_RESILIENCE_LIMITS;
  const retry = value?.retry ?? null;
  const breaker = value?.breaker ?? null;
  const timeout = value?.timeoutSeconds ?? null;

  const patch = (changes: Partial<ProxyResilience>) => onChange(patchResilience(value, changes));

  return (
    <div className="space-y-2">
      <Section
        title="Timeout"
        on={timeout !== null}
        switchLabel={`Set a timeout for ${subject}`}
        onToggle={(on) => patch({ timeoutSeconds: on ? startingTimeoutSeconds : null })}
        summary={
          timeout === null
            ? `Not set — each try stops after Blocks' own ${PROXY_PLATFORM_TIMEOUT_SECONDS}-second limit.`
            : "Counts from the first try. Waits between retries are not counted."
        }
      >
        <div className="flex items-end gap-2">
          <div className="w-28">
            <Label htmlFor={`${idPrefix}-timeout`} className="text-[11px]">
              Seconds
            </Label>
            <Input
              id={`${idPrefix}-timeout`}
              type="number"
              min={1}
              max={limits.maxTimeoutSeconds}
              className="h-8 text-xs"
              value={numberValue(timeout ?? 0)}
              onChange={(event) => patch({ timeoutSeconds: toNumber(event.target.value) })}
            />
          </div>
          <p className="pb-1.5 text-[11px] text-muted-foreground">
            1–{limits.maxTimeoutSeconds}. Your client sees a 504 when it runs out.
          </p>
        </div>
        <FieldError message={errors?.timeoutSeconds} />
      </Section>

      <Section
        title="Retries"
        on={retry !== null}
        switchLabel={`Retry failed calls for ${subject}`}
        onToggle={(on) => patch({ retry: on ? startingRetry() : null })}
        summary={
          retry === null
            ? "Off — a failed call is returned to your client as it came back."
            : "Retried on 429, 502, 503 and 504, and when a call does not complete. Any other answer — a 4xx included — comes back as it is."
        }
      >
        {retry ? (
          <>
            <div className="flex flex-wrap items-end gap-3">
              <div className="w-24">
                <Label htmlFor={`${idPrefix}-attempts`} className="text-[11px]">
                  Attempts
                </Label>
                <Input
                  id={`${idPrefix}-attempts`}
                  type="number"
                  min={1}
                  max={limits.maxRetryAttempts}
                  className="h-8 text-xs"
                  value={numberValue(retry.attempts)}
                  onChange={(event) =>
                    patch({ retry: { ...retry, attempts: toNumber(event.target.value) } })
                  }
                />
              </div>
              <div className="w-40">
                <Label htmlFor={`${idPrefix}-backoff`} className="text-[11px]">
                  Between attempts
                </Label>
                <Select
                  value={retry.backoff}
                  onValueChange={(next) =>
                    patch({ retry: { ...retry, backoff: next as ProxyBackoff } })
                  }
                >
                  <SelectTrigger id={`${idPrefix}-backoff`} className="h-8 text-xs">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {BACKOFF_LABELS.map((option) => (
                      <SelectItem key={option.value} value={option.value} className="text-xs">
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              {retry.backoff === "none" ? null : (
                <div className="w-24">
                  <Label htmlFor={`${idPrefix}-delay`} className="text-[11px]">
                    First wait (s)
                  </Label>
                  <Input
                    id={`${idPrefix}-delay`}
                    type="number"
                    min={1}
                    max={limits.maxRetryDelaySeconds}
                    className="h-8 text-xs"
                    value={numberValue(retry.initialDelaySeconds)}
                    onChange={(event) =>
                      patch({
                        retry: { ...retry, initialDelaySeconds: toNumber(event.target.value) },
                      })
                    }
                  />
                </div>
              )}
            </div>
            <p className="text-[11px] text-muted-foreground">
              {BACKOFF_LABELS.find((option) => option.value === retry.backoff)?.hint} Attempts count
              the first call. With a timeout set, all tries share it; the waits between them are
              not counted.
            </p>
            <FieldError message={errors?.["retry.attempts"]} />
            <FieldError message={errors?.["retry.initialDelaySeconds"]} />

            {/*
              Not a formality. The platform cannot tell whether this vendor tolerates the same request
              twice, the API refuses retries without the claim, and getting it wrong is how somebody is
              charged twice — so it is asked as a question with its consequence attached.
            */}
            <label
              htmlFor={`${idPrefix}-idempotent`}
              className="flex cursor-pointer items-start gap-2 rounded-md border border-amber-200 bg-amber-50/60 p-2"
            >
              <Checkbox
                id={`${idPrefix}-idempotent`}
                aria-label="Sending this request twice is safe"
                className="mt-0.5"
                checked={retry.idempotent}
                onCheckedChange={(checked) =>
                  patch({ retry: { ...retry, idempotent: checked === true } })
                }
              />
              <span className="min-w-0">
                <span className="flex items-center gap-1.5 text-[11px] font-medium">
                  <AlertTriangle className="h-3 w-3 text-amber-600" />
                  Sending this request twice is safe
                </span>
                <span className="block text-[11px] text-muted-foreground">
                  A retry sends the same request again, so the vendor may act on it twice. Reads are
                  safe. A payment, an order or an email usually is not — unless the vendor dedupes it
                  for you.
                </span>
              </span>
            </label>
            <FieldError message={errors?.["retry.idempotent"]} />
          </>
        ) : null}
      </Section>

      <Section
        title="Circuit breaker"
        on={breaker !== null}
        switchLabel={`Pause calls to an unreachable vendor for ${subject}`}
        onToggle={(on) => patch({ breaker: on ? startingBreaker() : null })}
        summary={
          breaker === null
            ? "Off — every call is attempted, however long the vendor has been unreachable."
            : "While paused your client gets an immediate 503 instead of waiting."
        }
      >
        {breaker ? (
          <>
            <div className="flex flex-wrap items-end gap-3">
              <div className="w-28">
                <Label htmlFor={`${idPrefix}-threshold`} className="text-[11px]">
                  Failures
                </Label>
                <Input
                  id={`${idPrefix}-threshold`}
                  type="number"
                  min={1}
                  max={limits.maxBreakerThreshold}
                  className="h-8 text-xs"
                  value={numberValue(breaker.failureThreshold)}
                  onChange={(event) =>
                    patch({
                      breaker: { ...breaker, failureThreshold: toNumber(event.target.value) },
                    })
                  }
                />
              </div>
              <div className="w-28">
                <Label htmlFor={`${idPrefix}-open`} className="text-[11px]">
                  Pause for (s)
                </Label>
                <Input
                  id={`${idPrefix}-open`}
                  type="number"
                  min={1}
                  max={limits.maxBreakerOpenSeconds}
                  className="h-8 text-xs"
                  value={numberValue(breaker.openSeconds)}
                  onChange={(event) =>
                    patch({ breaker: { ...breaker, openSeconds: toNumber(event.target.value) } })
                  }
                />
              </div>
            </div>
            <p className="text-[11px] text-muted-foreground">
              After {breaker.failureThreshold || "…"} calls in a row the vendor did not answer — it
              refused the connection, or ran out of time — calls to it stop for{" "}
              {breaker.openSeconds || "…"} seconds. One is then let through to see whether it is
              back. A vendor that answers is not a failure here, even when it answers with an error.
              The count is per vendor host for this proxy, kept separately for public callers,
              signed-in callers, workflows and tests.
            </p>
            <FieldError message={errors?.["breaker.failureThreshold"]} />
            <FieldError message={errors?.["breaker.openSeconds"]} />
          </>
        ) : null}
      </Section>
    </div>
  );
};
