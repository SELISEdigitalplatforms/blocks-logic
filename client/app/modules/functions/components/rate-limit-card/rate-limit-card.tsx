import { useState } from "react";
import { Gauge } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import type { AuthMode } from "../../types/function.types";

/** The platform's default for a Public function (Functions:RateLimits:PublicPerMinute). */
export const PUBLIC_DEFAULT_PER_MINUTE = 600;
export const MAX_PER_MINUTE = 100_000;

type RateLimitCardProps = {
  authMode: AuthMode;
  /** The tenant's own limit; null or empty means "the default for this access". */
  value: number | null | undefined;
  onChange: (value: number | null) => void;
};

/**
 * HTTP calls per minute for this function (FN-19). Counted per function, not per caller IP: behind
 * a proxy every caller can look like one IP, and a forwarded IP can be faked. Only HTTP calls
 * count — workflow steps and Test runs never do. Public functions get a default so a leaked URL
 * cannot run up the tenant's bill; Token ones have none unless the tenant sets one.
 */
export const RateLimitCard = ({ authMode, value, onChange }: RateLimitCardProps) => {
  const [draft, setDraft] = useState(value == null ? "" : String(value));
  const [error, setError] = useState<string | null>(null);
  const isPublic = authMode === "Public";

  const onInput = (text: string) => {
    setDraft(text);
    if (text.trim() === "") {
      setError(null);
      return onChange(null);
    }
    const parsed = Number(text);
    if (!Number.isInteger(parsed) || parsed < 1 || parsed > MAX_PER_MINUTE) {
      return setError(`A whole number from 1 to ${MAX_PER_MINUTE.toLocaleString("en")}.`);
    }
    setError(null);
    onChange(parsed);
  };

  const effective =
    value != null
      ? `${value.toLocaleString("en")} calls a minute (your limit).`
      : isPublic
        ? `${PUBLIC_DEFAULT_PER_MINUTE} calls a minute (the default for public functions).`
        : "No limit (the default for functions that need a login).";

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-5">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="flex items-center gap-1.5 text-base font-semibold">
            <Gauge className="h-4 w-4 text-muted-foreground" />
            Rate limit
          </span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            HTTP calls per minute for this function, from all callers together. Over it, callers get
            429 with Retry-After; nothing runs and nothing is saved. Workflow steps and Test runs are
            never counted.
          </span>
        </div>
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">Calls per minute</span>
          <Input
            type="number"
            inputMode="numeric"
            min={1}
            max={MAX_PER_MINUTE}
            placeholder={isPublic ? `Default: ${PUBLIC_DEFAULT_PER_MINUTE}` : "Default: no limit"}
            value={draft}
            onChange={(event) => onInput(event.target.value)}
            aria-invalid={error ? true : undefined}
            data-testid="rate-limit-input"
            className="max-w-48"
          />
        </label>
        {error ? (
          <p className="text-xs text-error" role="alert">
            {error}
          </p>
        ) : (
          <p className="text-xs text-medium-emphasis" data-testid="rate-limit-effective">
            Now: {effective} Takes effect when you deploy.
          </p>
        )}
      </CardContent>
    </Card>
  );
};
