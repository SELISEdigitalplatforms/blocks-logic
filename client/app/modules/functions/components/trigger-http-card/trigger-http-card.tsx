import { Globe, Hourglass, KeyRound, Radio, Zap } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { cn } from "@/lib/utils";
import { AccessRulePicker } from "@/modules/proxy/components/proxy-access-card";
import { useIamPermissions, useIamRoles } from "@/modules/proxy/hooks";
import type { ProxyAccessRule } from "@/modules/proxy/types";
import { FUNCTION_HTTP_VERBS, acceptedHttpVerbs } from "../../constants/endpoint.constant";
import {
  AccessCombine,
  AuthMode,
  HttpTriggerVerb,
  ITriggerConfig,
  MatchMode,
  TriggerResponseMode,
} from "../../types/function.types";
import { describeTriggerAccess } from "../../utils/access";
import { EndpointBadge } from "../endpoint-badge";
import { renderRichText } from "@/lib/rich-text";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const KIND_OPTIONS: Array<{
  value: AuthMode;
  title: string;
  description: string;
  icon: typeof KeyRound;
}> = [
  {
    value: "Token",
    get title() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.BLOCKS_TOKEN");
    },
    get description() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.THE_CALLER_SENDS_A");
    },
    icon: KeyRound,
  },
  {
    value: "Public",
    get title() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.PUBLIC");
    },
    get description() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.ANYONE_WITH_THE_URL");
    },
    icon: Globe,
  },
];

const RESPONSE_OPTIONS: Array<{
  value: TriggerResponseMode;
  title: string;
  description: string;
  icon: typeof KeyRound;
}> = [
  {
    value: "async",
    get title() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.BACKGROUND_202_POLL");
    },
    get description() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.THE_CALLER_GETS_202");
    },
    icon: Hourglass,
  },
  {
    value: "sync",
    get title() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.WAIT_FOR_ANSWER_API");
    },
    get description() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.THE_CALLER_GETS_THE");
    },
    icon: Zap,
  },
  {
    value: "sse",
    get title() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.STREAM_EVENTS_SSE");
    },
    get description() {
      return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.THE_CALLER_GETS_TWO");
    },
    icon: Radio,
  },
];

/** What the HTTP endpoint answers, in one sentence per response mode. */
const ENDPOINT_HINTS: Record<TriggerResponseMode, string> = {
  get async() {
    return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.ALWAYS_ON_THE_CALL");
  },
  get sync() {
    return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.ALWAYS_ON_THE_CALLER");
  },
  get sse() {
    return tStatic("logic.functions.components", "TRIGGER_HTTP_CARD.ALWAYS_ON_THE_CALLER_2");
  },
};

/**
 * The only method editor. There used to be a GET/POST switch beside it too, but it edited the same
 * setting (it showed only while one GET or POST was on) — two controls for one thing.
 *
 * The verbs after one is toggled, as the trigger stores them. A single GET or POST goes back to
 * the legacy single `httpMethod` (and an empty list), so a function that only ever needed one
 * method keeps the shape every older function has. The last verb cannot be removed.
 */
const toggleVerb = (
  value: ITriggerConfig,
  verb: HttpTriggerVerb,
): Partial<ITriggerConfig> | null => {
  const current = acceptedHttpVerbs(value) as HttpTriggerVerb[];
  const next = current.includes(verb)
    ? current.filter((item) => item !== verb)
    : FUNCTION_HTTP_VERBS.filter((item) => item === verb || current.includes(item));
  if (next.length === 0) return null;
  if (next.length === 1 && (next[0] === "GET" || next[0] === "POST")) {
    return { httpMethods: [], httpMethod: next[0] === "GET" ? "Get" : "Post" };
  }
  return { httpMethods: next };
};

/** The trigger's list + its any/all as the shared picker's rule shape, and back. */
const toRule = (values: string[], match: MatchMode): ProxyAccessRule => ({
  mode: match === "All" ? "all" : "any",
  values,
});
const fromRule = (rule: ProxyAccessRule): { values: string[]; match: MatchMode } => ({
  values: rule.values,
  match: rule.mode === "all" ? "All" : "Any",
});

type TriggerHttpCardProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
  functionId: string;
};

/**
 * The HTTP endpoint and its "Who can call it", laid out exactly like a proxy's access card so the
 * two products read as one: the same two kinds with the same icons, the same "Restrict further"
 * panel with one OR/AND between the lists and an any/all inside each, the same IAM pickers, the
 * same footer sentence. HTTP itself is always on — a function with no endpoint has no way in.
 *
 * The shape underneath differs from a proxy's (`roles` + `roleMatch` rather than a rule object),
 * so this card translates at its edges and nothing else has to know.
 */
export const TriggerHttpCard = ({ value, onChange, functionId }: TriggerHttpCardProps) => {
  const { t } = useT("logic.functions.components");
  const patch = (partial: Partial<ITriggerConfig>) => onChange({ ...value, ...partial });
  const rolesQuery = useIamRoles();
  const permissionsQuery = useIamPermissions();
  const isToken = value.authMode === "Token";
  const hasVerbList = (value.httpMethods ?? []).length > 0;
  const accepted = acceptedHttpVerbs(value);

  const onResponseModeChange = (mode: string) => {
    // Cookies only apply to a sync answer; clearing on the way out keeps a hidden "on" from
    // coming back the next time someone picks "Wait for answer".
    if (mode === "sync" || mode === "sse") patch({ responseMode: mode });
    else patch({ responseMode: "async" });
  };

  const onKindChange = (kind: string) => {
    if (kind === "Public") {
      // Public ignores rules, and the server drops them anyway; clearing here means the card
      // shows what will be enforced rather than chips that no longer do anything.
      patch({ authMode: "Public", roles: [], permissions: [] });
    } else {
      patch({ authMode: "Token" });
    }
  };

  return (
    <Card className="rounded-xl">
      <CardContent className="space-y-5 p-5">
        <div className="space-y-3">
          <div>
            <p className="text-sm font-semibold">{t("TRIGGER_HTTP_CARD.HTTP_ENDPOINT")}</p>
            <p className="text-xs text-muted-foreground">
              {ENDPOINT_HINTS[value.responseMode] ?? ENDPOINT_HINTS.async}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs text-muted-foreground">
              {t("TRIGGER_HTTP_CARD.ACCEPTED_METHODS")}
            </span>
            <div
              className="flex flex-wrap gap-1"
              role="group"
              aria-label={t("TRIGGER_HTTP_CARD.ACCEPTED_METHODS")}
            >
              {FUNCTION_HTTP_VERBS.map((verb) => {
                const pressed = accepted.includes(verb);
                // The last accepted verb cannot be removed; say so instead of silently ignoring.
                const isLast = pressed && accepted.length === 1;
                return (
                  <button
                    key={verb}
                    type="button"
                    aria-pressed={pressed}
                    aria-disabled={isLast || undefined}
                    title={isLast ? t("TRIGGER_HTTP_CARD.AT_LEAST_ONE_METHOD") : undefined}
                    aria-label={t("TRIGGER_HTTP_CARD.ACCEPT", { verb })}
                    className={cn(
                      "rounded-md border px-2 py-0.5 font-mono text-xs transition-colors",
                      pressed
                        ? "border-primary bg-primary/5 font-semibold text-primary"
                        : "text-muted-foreground hover:bg-muted/40",
                    )}
                    onClick={() => {
                      const next = toggleVerb(value, verb);
                      if (next) patch(next);
                    }}
                  >
                    {verb}
                  </button>
                );
              })}
            </div>
          </div>
          <EndpointBadge
            functionId={functionId}
            method={value.httpMethod}
            methods={hasVerbList ? accepted : undefined}
          />
        </div>

        <div className="space-y-3">
          <div>
            <p className="text-sm font-semibold">{t("TRIGGER_HTTP_CARD.RESPONSE")}</p>
            <p className="text-xs text-muted-foreground">
              {t("TRIGGER_HTTP_CARD.WHAT_AN_HTTP_CALLER")}
            </p>
          </div>
          <RadioGroup
            value={value.responseMode}
            onValueChange={onResponseModeChange}
            className="gap-2"
            aria-label={t("TRIGGER_HTTP_CARD.RESPONSE")}
          >
            {RESPONSE_OPTIONS.map((option) => {
              const Icon = option.icon;
              const checked = value.responseMode === option.value;
              return (
                <label
                  key={option.value}
                  className={cn(
                    "flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors",
                    checked ? "border-primary bg-primary/5" : "hover:bg-muted/40",
                  )}
                >
                  <RadioGroupItem
                    value={option.value}
                    aria-label={option.title}
                    className="mt-0.5"
                  />
                  <div className="min-w-0">
                    <p className="flex items-center gap-1.5 text-sm font-medium">
                      <Icon className="h-3.5 w-3.5 text-muted-foreground" />
                      {option.title}
                    </p>
                    <p className="text-xs text-muted-foreground">{option.description}</p>
                  </div>
                </label>
              );
            })}
          </RadioGroup>
        </div>

        <div className="space-y-4">
          <div>
            <p className="text-sm font-semibold">{t("TRIGGER_HTTP_CARD.WHO_CAN_CALL_IT")}</p>
            <p className="text-xs text-muted-foreground">
              {t("TRIGGER_HTTP_CARD.ONE_SETTING_FOR_EVERY")}
            </p>
          </div>

          <RadioGroup value={value.authMode} onValueChange={onKindChange} className="gap-2">
            {KIND_OPTIONS.map((option) => {
              const Icon = option.icon;
              const checked = value.authMode === option.value;
              return (
                <label
                  key={option.value}
                  className={cn(
                    "flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors",
                    checked ? "border-primary bg-primary/5" : "hover:bg-muted/40",
                  )}
                >
                  <RadioGroupItem
                    value={option.value}
                    aria-label={option.title}
                    className="mt-0.5"
                  />
                  <div className="min-w-0">
                    <p className="flex items-center gap-1.5 text-sm font-medium">
                      <Icon className="h-3.5 w-3.5 text-muted-foreground" />
                      {option.title}
                    </p>
                    <p className="text-xs text-muted-foreground">{option.description}</p>
                  </div>
                </label>
              );
            })}
          </RadioGroup>

          {isToken ? (
            <div
              className="space-y-4 rounded-lg border bg-muted/20 p-4"
              data-testid="access-restrictions"
            >
              <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <p className="text-sm">
                  <span className="font-semibold">{t("TRIGGER_HTTP_CARD.RESTRICT_FURTHER")}</span>
                  <span className="text-muted-foreground">
                    {" "}
                    {t("TRIGGER_HTTP_CARD.OPTIONAL_BOTH_WORK_TOGETHER")}
                  </span>
                </p>
                <Tabs
                  value={value.combine}
                  onValueChange={(combine) => patch({ combine: combine as AccessCombine })}
                  className="w-auto flex-shrink-0"
                >
                  <TabsList
                    className="grid h-8 grid-cols-2 rounded-md bg-muted p-0.5"
                    aria-label={t("TRIGGER_HTTP_CARD.HOW_ROLES_AND_PERMISSIONS")}
                  >
                    <TabsTrigger value="Or" className="rounded px-3 text-xs">
                      OR
                    </TabsTrigger>
                    <TabsTrigger value="And" className="rounded px-3 text-xs">
                      AND
                    </TabsTrigger>
                  </TabsList>
                </Tabs>
              </div>

              <AccessRulePicker
                label={t("TRIGGER_HTTP_CARD.ROLES")}
                kind="role"
                addLabel={t("TRIGGER_HTTP_CARD.ADD_ROLE")}
                rule={toRule(value.roles, value.roleMatch)}
                options={rolesQuery.data ?? []}
                loading={rolesQuery.isLoading}
                onChange={(rule) => {
                  const { values, match } = fromRule(rule);
                  patch({ roles: values, roleMatch: match });
                }}
              />
              <AccessRulePicker
                label={t("TRIGGER_HTTP_CARD.PERMISSIONS")}
                kind="permission"
                addLabel={t("TRIGGER_HTTP_CARD.ADD_PERMISSION")}
                rule={toRule(value.permissions, value.permissionMatch)}
                options={permissionsQuery.data ?? []}
                loading={permissionsQuery.isLoading}
                onChange={(rule) => {
                  const { values, match } = fromRule(rule);
                  patch({ permissions: values, permissionMatch: match });
                }}
              />

              <p className="text-xs text-muted-foreground" data-testid="access-summary">
                {describeTriggerAccess(value)}
              </p>
            </div>
          ) : (
            <p className="text-xs text-muted-foreground" data-testid="access-summary">
              {renderRichText(
                t("TRIGGER_HTTP_CARD.N_0_CTX_CONTEXT_USERID", {
                  value: describeTriggerAccess(value),
                }),
                [
                  (children) => <code className="font-mono">{children}</code>,
                  (children) => <code className="font-mono">{children}</code>,
                  (children) => <code className="font-mono">{children}</code>,
                  (children) => <code className="font-mono">{children}</code>,
                ],
              )}
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  );
};
