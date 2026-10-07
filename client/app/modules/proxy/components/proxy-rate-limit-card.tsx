import { Control, useController, useWatch } from "react-hook-form";
import { Gauge } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Input } from "@/components/ui-kits/input/input";
import { ProxyFormValues } from "../types";
import { PROXY_MAX_PER_MINUTE, PROXY_PUBLIC_DEFAULT_PER_MINUTE } from "../utils";

type Props = {
  control: Control<ProxyFormValues>;
};

/**
 * Gateway calls per minute for this proxy (P-3), the same rule as a function's (FN-19). Counted per
 * proxy, not per caller IP: behind a load balancer every caller can look like one IP, and a forwarded
 * IP can be faked. Public proxies get a default so a URL read out of a web page cannot spend the
 * vendor budget without limit; token proxies have none unless the tenant sets one.
 */
export const ProxyRateLimitCard = ({ control }: Props) => {
  const { field, fieldState } = useController({ control, name: "requestsPerMinute" });
  const kind = useWatch({ control, name: "access.kind" });
  const isPublic = kind === "public";
  const value = field.value ?? null;

  const onInput = (text: string) => {
    // Empty ⇒ the default. Anything else goes to the schema as typed, so a bad number shows the
    // schema's message and blocks Save instead of being silently dropped.
    field.onChange(text.trim() === "" ? null : Number(text));
  };

  const effective =
    value != null && !fieldState.error
      ? `${value.toLocaleString("en")} calls a minute (your limit).`
      : isPublic
        ? `${PROXY_PUBLIC_DEFAULT_PER_MINUTE} calls a minute (the default for public proxies).`
        : "No limit (the default for proxies that need a Blocks token).";

  return (
    <Card className="rounded-xl">
      <CardContent className="flex flex-col gap-3 p-4">
        <div className="flex min-w-0 flex-col gap-1">
          <span className="flex items-center gap-1.5 text-base font-semibold">
            <Gauge className="h-4 w-4 text-muted-foreground" />
            Rate limit
          </span>
          <span className="text-xs leading-relaxed text-medium-emphasis">
            Calls per minute through the gateway, from all callers together. Over it, callers get 429
            with Retry-After; nothing is sent to the vendor and nothing is logged. Workflow steps and
            Test calls are never counted.
          </span>
        </div>
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">Calls per minute</span>
          <Input
            type="number"
            inputMode="numeric"
            min={1}
            max={PROXY_MAX_PER_MINUTE}
            placeholder={isPublic ? `Default: ${PROXY_PUBLIC_DEFAULT_PER_MINUTE}` : "Default: no limit"}
            value={value ?? ""}
            onChange={(event) => onInput(event.target.value)}
            onBlur={field.onBlur}
            aria-invalid={fieldState.error ? true : undefined}
            data-testid="proxy-rate-limit-input"
            className="max-w-48"
          />
        </label>
        {fieldState.error ? (
          <p className="text-xs text-error" role="alert">
            {fieldState.error.message}
          </p>
        ) : (
          <p className="text-xs text-medium-emphasis" data-testid="proxy-rate-limit-effective">
            Now: {effective} Takes effect when you save.
          </p>
        )}
      </CardContent>
    </Card>
  );
};
