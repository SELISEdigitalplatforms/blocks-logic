import { type ReactNode } from "react";
import { type FieldPathValue, type UseFormReturn, useWatch } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { ProxyFormValues } from "../types";
import { useT } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

type Props = {
  form: UseFormReturn<ProxyFormValues>;
};

type ApiOnlyField = "responseMode" | "responseInclude" | "bodyMerge" | "bodyMode" | "methodConfigs";

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
}) => {
  const { t } = useT("logic.proxy.components");
  return (
    <li className="flex flex-wrap items-start justify-between gap-4">
      <div className="space-y-1">
        <p className="text-sm font-medium">{title}</p>
        <p className="text-xs text-muted-foreground">{children}</p>
      </div>
      <Button type="button" variant="outline" size="sm" aria-label={t("PROXY_API_ONLY_SETTINGS_CARD.TURN_OFF_2", { title })} onClick={onTurnOff}>
        {t("PROXY_API_ONLY_SETTINGS_CARD.TURN_OFF")}
      </Button>
    </li>
  );
};

/**
 * Proxy-wide settings the console does not edit — response filter, body merge and per-method overrides —
 * but the CLI and API can set. The console keeps them on every save; this card makes them visible and lets
 * the owner clear one on purpose. Values are not shown: a body-merge value may hold a credential.
 */
export const ProxyApiOnlySettingsCard = ({ form }: Props) => {
  const { t } = useT("logic.proxy.components");
  const [responseMode, responseInclude, bodyMerge, methodConfigs] = useWatch({
    control: form.control,
    name: ["responseMode", "responseInclude", "bodyMerge", "methodConfigs"],
  });
  const include = responseInclude ?? [];
  const bodyKeys = (bodyMerge ?? []).map((row) => row.key).filter(Boolean);
  const overrides = methodConfigs ?? [];

  const filterOn = responseMode === "select";
  if (!filterOn && !bodyKeys.length && !overrides.length) return null;

  const clear = <K extends ApiOnlyField>(
    name: K,
    value: FieldPathValue<ProxyFormValues, K>,
  ) => form.setValue(name, value, { shouldDirty: true });

  return (
    <Card className="rounded-xl" data-testid="proxy-api-only-settings">
      <CardContent className="space-y-4 p-0">
        <div>
          <p className="text-sm font-semibold">{t("PROXY_API_ONLY_SETTINGS_CARD.PROXY_WIDE_SETTINGS_FROM")}</p>
          <p className="text-xs text-muted-foreground">
            {t("PROXY_API_ONLY_SETTINGS_CARD.THESE_ARE_KEPT_WHEN")}
          </p>
        </div>
        <ul className="space-y-4">
          {filterOn ? (
            <Row
              title={t("PROXY_API_ONLY_SETTINGS_CARD.PROXY_WIDE_RESPONSE_FILTER")}
              onTurnOff={() => {
                clear("responseMode", "all");
                clear("responseInclude", []);
              }}
            >
              {include.length
                ? renderRichText(t("PROXY_API_ONLY_SETTINGS_CARD.RETURN_ONLY_FIELDS", { fields: include.join(", ") }), [(text: string) => <code>{text}</code>])
                : renderRichText(t("PROXY_API_ONLY_SETTINGS_CARD.RETURN_EMPTY_OBJECT"), [(text: string) => <code>{text}</code>])}
            </Row>
          ) : null}
          {bodyKeys.length ? (
            <Row
              title={t("PROXY_API_ONLY_SETTINGS_CARD.PROXY_WIDE_BODY_FIELDS")}
              onTurnOff={() => {
                clear("bodyMerge", []);
                clear("bodyMode", "passthrough");
              }}
            >
              {t("PROXY_API_ONLY_SETTINGS_CARD.POST_PUT_AND_PATCH")}<Codes items={bodyKeys} /> {t("PROXY_API_ONLY_SETTINGS_CARD.ADDED_TO_THE_REQUEST")}
            </Row>
          ) : null}
          {overrides.length ? (
            <Row title={t("PROXY_API_ONLY_SETTINGS_CARD.PER_METHOD_OVERRIDES_ARE")} onTurnOff={() => clear("methodConfigs", [])}>
              <Codes items={overrides.map((entry) => entry.method)} /> {t("PROXY_API_ONLY_SETTINGS_CARD.CALLS_USE_THEIR_OWN")}
            </Row>
          ) : null}
        </ul>
      </CardContent>
    </Card>
  );
};
