import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import type { AuthMode } from "../../types/function.types";
import { useT } from "@seliseblocks/genesis-os";

/** The platform's default for a Public function (Functions:RateLimits:PublicPerMinute). */
export const PUBLIC_DEFAULT_PER_MINUTE = 600;
export const MAX_PER_MINUTE = 100_000;
const RATE_LIMIT_INPUT_ID = "trigger-rate-limit";

type RateLimitFieldProps = {
  authMode: AuthMode;
  /** The tenant's own limit; null or empty means "the default for this access". */
  value: number | null | undefined;
  onChange: (value: number | null) => void;
};

/**
 * "Max calls per minute" (FN-19): HTTP calls per minute for this function. Counted per function,
 * not per caller IP: behind a proxy every caller can look like one IP, and a forwarded IP can be faked. Only HTTP calls
 * count — workflow steps and Test runs never do. Public functions get a default so a leaked URL
 * cannot run up the tenant's bill; Token ones have none unless the tenant sets one.
 */
export const RateLimitField = ({ authMode, value, onChange }: RateLimitFieldProps) => {
  const { t } = useT("logic.functions.components");
  const [draft, setDraft] = useState(value == null ? "" : String(value));
  const [error, setError] = useState<string | null>(null);
  const isPublic = authMode === "Public";

  const onInput = (text: string) => {
    setDraft(text);
    if (text.trim() === "") {
      setError(null);
      onChange(null);
      return;
    }
    const parsed = Number(text);
    if (!Number.isInteger(parsed) || parsed < 1 || parsed > MAX_PER_MINUTE) {
      setError(
        t("RATE_LIMIT_CARD.A_WHOLE_NUMBER_FROM", { value: MAX_PER_MINUTE.toLocaleString("en") }),
      );
      return;
    }
    setError(null);
    onChange(parsed);
  };

  let effective = t("RATE_LIMIT_CARD.NO_LIMIT_THE_DEFAULT");
  if (value != null)
    effective = t("RATE_LIMIT_CARD.CALLS_A_MINUTE_YOUR_LIMIT", {
      value: value.toLocaleString("en"),
    });
  else if (isPublic)
    effective = t("RATE_LIMIT_CARD.CALLS_A_MINUTE_PUBLIC_DEFAULT", {
      value: PUBLIC_DEFAULT_PER_MINUTE,
    });

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex flex-wrap items-center gap-3">
        <label htmlFor={RATE_LIMIT_INPUT_ID} className="text-sm font-medium">
          {t("RATE_LIMIT_CARD.CALLS_PER_MINUTE")}
        </label>
        <Input
          id={RATE_LIMIT_INPUT_ID}
          type="number"
          inputMode="numeric"
          min={1}
          max={MAX_PER_MINUTE}
          placeholder={
            isPublic
              ? t("RATE_LIMIT_CARD.DEFAULT", { publicDefaultPerMinute: PUBLIC_DEFAULT_PER_MINUTE })
              : t("RATE_LIMIT_CARD.DEFAULT_NO_LIMIT")
          }
          value={draft}
          onChange={(event) => onInput(event.target.value)}
          aria-invalid={error ? true : undefined}
          data-testid="rate-limit-input"
          className="max-w-48"
        />
      </div>
      {error ? (
        <p className="text-xs text-error" role="alert">
          {error}
        </p>
      ) : (
        <p className="text-xs text-medium-emphasis" data-testid="rate-limit-effective">
          {t("RATE_LIMIT_CARD.NOW_TAKES_EFFECT_WHEN", { effective })}
        </p>
      )}
    </div>
  );
};
