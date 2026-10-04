import { Control, useController } from "react-hook-form";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { ProxyFormValues } from "../types";
import { ProxyResilienceFields, ResilienceErrors } from "./proxy-resilience-fields";

type Props = {
  control: Control<ProxyFormValues>;
};

/**
 * Pulls the messages the schema raised for this object out of the form's error tree and flattens them
 * to the keys {@link ProxyResilienceFields} reads — `retry.attempts` rather than a nested object.
 */
export const flattenResilienceErrors = (errors: unknown): ResilienceErrors => {
  const tree = (errors ?? {}) as Record<string, { message?: string } & Record<string, unknown>>;
  const flat: ResilienceErrors = {};

  const read = (node: unknown): string | undefined =>
    typeof (node as { message?: unknown } | undefined)?.message === "string"
      ? ((node as { message: string }).message)
      : undefined;

  flat.timeoutSeconds = read(tree.timeoutSeconds);
  (["attempts", "initialDelaySeconds", "idempotent"] as const).forEach((key) => {
    flat[`retry.${key}`] = read((tree.retry as Record<string, unknown> | undefined)?.[key]);
  });
  (["failureThreshold", "openSeconds"] as const).forEach((key) => {
    flat[`breaker.${key}`] = read((tree.breaker as Record<string, unknown> | undefined)?.[key]);
  });

  return flat;
};

/**
 * "If the vendor is slow or down" — the proxy-wide policy every endpoint inherits.
 *
 * It sits beside "Who can call it" rather than inside the endpoint list because it is a property of
 * the vendor, not of one call: the breaker in particular counts failures per vendor host, so every
 * endpoint pointing at it trips together. An endpoint that genuinely differs overrides it in its own
 * row, and an override replaces this whole policy rather than merging into it — which is how the
 * gateway reads it, and so what the copy here has to say.
 */
export const ProxyResilienceCard = ({ control }: Props) => {
  // `fieldState.error` for this name is the whole subtree the schema raised — `timeoutSeconds`,
  // `retry.attempts` and so on — which is what the fields below need to place each message.
  const { field, fieldState } = useController({ control, name: "resilience" });

  return (
    <Card className="rounded-xl">
      <CardContent className="space-y-4 p-0">
        <div>
          <p className="text-sm font-semibold">If the vendor is slow or down</p>
          <p className="text-xs text-muted-foreground">
            Nothing here is on unless you turn it on, and nothing is chosen for you. Left alone, a
            call waits for the vendor, is never sent twice, and a vendor that is down is called every
            time anyway.
          </p>
        </div>

        <ProxyResilienceFields
          idPrefix="proxy-resilience"
          subject="this proxy"
          value={field.value ?? null}
          onChange={field.onChange}
          errors={flattenResilienceErrors(fieldState.error)}
        />
      </CardContent>
    </Card>
  );
};
