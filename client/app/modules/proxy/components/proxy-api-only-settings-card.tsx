import { type ReactNode } from "react";
import { type PathValue, type UseFormReturn, useWatch } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { ProxyFormValues } from "../types";

type Props = {
  form: UseFormReturn<ProxyFormValues>;
};

const Codes = ({ items }: { items: string[] }) => (
  <>
    {items.map((item, index) => (
      <span key={`${item}-${index}`}>
        {index ? ", " : null}
        <code>{item}</code>
      </span>
    ))}
  </>
);

const Row = ({
  title,
  children,
  onTurnOff,
}: {
  title: string;
  children: ReactNode;
  onTurnOff: () => void;
}) => (
  <li className="flex flex-wrap items-start justify-between gap-4">
    <div className="space-y-1">
      <p className="text-sm font-medium">{title}</p>
      <p className="text-xs text-muted-foreground">{children}</p>
    </div>
    <Button type="button" variant="outline" size="sm" aria-label={`Turn off: ${title}`} onClick={onTurnOff}>
      Turn off
    </Button>
  </li>
);

/**
 * Proxy-wide settings the console does not edit — response filter, body merge and per-method overrides —
 * but the CLI and API can set. The console keeps them on every save; this card makes them visible and lets
 * the owner clear one on purpose. Values are not shown: a body-merge value may hold a credential.
 */
export const ProxyApiOnlySettingsCard = ({ form }: Props) => {
  const [responseMode, responseInclude, bodyMerge, methodConfigs] = useWatch({
    control: form.control,
    name: ["responseMode", "responseInclude", "bodyMerge", "methodConfigs"],
  });
  const include = responseInclude ?? [];
  const bodyKeys = (bodyMerge ?? []).map((row) => row.key).filter(Boolean);
  const overrides = methodConfigs ?? [];

  const filterOn = responseMode === "select";
  if (!filterOn && !bodyKeys.length && !overrides.length) return null;

  const clear = <K extends "responseMode" | "responseInclude" | "bodyMerge" | "bodyMode" | "methodConfigs">(
    name: K,
    value: PathValue<ProxyFormValues, K>,
  ) => form.setValue(name, value, { shouldDirty: true });

  return (
    <Card className="rounded-xl" data-testid="proxy-api-only-settings">
      <CardContent className="space-y-4 p-0">
        <div>
          <p className="text-sm font-semibold">Proxy-wide settings from the CLI or API</p>
          <p className="text-xs text-muted-foreground">
            These are kept when you save. They apply to every endpoint that does not set its own.
          </p>
        </div>
        <ul className="space-y-4">
          {filterOn ? (
            <Row
              title="Proxy-wide response filter is on"
              onTurnOff={() => {
                clear("responseMode", "all");
                clear("responseInclude", []);
              }}
            >
              Endpoints without their own filter return only{" "}
              {include.length ? (
                <Codes items={include} />
              ) : (
                <>
                  an empty object (<code>{"{}"}</code>)
                </>
              )}
              .
            </Row>
          ) : null}
          {bodyKeys.length ? (
            <Row
              title="Proxy-wide body fields are on"
              onTurnOff={() => {
                clear("bodyMerge", []);
                clear("bodyMode", "passthrough");
              }}
            >
              POST, PUT and PATCH calls on endpoints without their own body fields get{" "}
              <Codes items={bodyKeys} /> added to the request body.
            </Row>
          ) : null}
          {overrides.length ? (
            <Row title="Per-method overrides are on" onTurnOff={() => clear("methodConfigs", [])}>
              <Codes items={overrides.map((entry) => entry.method)} /> calls use their own upstream,
              headers or query.
            </Row>
          ) : null}
        </ul>
      </CardContent>
    </Card>
  );
};
