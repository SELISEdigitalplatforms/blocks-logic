import { Globe, Hourglass, KeyRound, Zap } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { cn } from "@/lib/utils";
import { AccessRulePicker } from "@/modules/proxy/components/proxy-access-card";
import { useIamPermissions, useIamRoles } from "@/modules/proxy/hooks";
import type { ProxyAccessRule } from "@/modules/proxy/types";
import {
  FUNCTION_HTTP_METHODS,
  FUNCTION_HTTP_VERBS,
  acceptedHttpVerbs,
  toHttpVerb,
} from "../../constants/endpoint.constant";
import {
  AccessCombine,
  AuthMode,
  HttpTriggerMethod,
  HttpTriggerVerb,
  ITriggerConfig,
  MatchMode,
  TriggerResponseMode,
} from "../../types/function.types";
import { describeTriggerAccess } from "../../utils/access";
import { EndpointBadge } from "../endpoint-badge";

const KIND_OPTIONS: Array<{
  value: AuthMode;
  title: string;
  description: string;
  icon: typeof KeyRound;
}> = [
  {
    value: "Token",
    title: "Blocks token",
    description:
      "The caller sends a Blocks token. Identity, roles and permissions arrive on ctx.context.",
    icon: KeyRound,
  },
  {
    value: "Public",
    title: "Public",
    description:
      "Anyone with the URL and your x-blocks-key can call it. No identity, no token-scoped work.",
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
    title: "Background (202 + poll)",
    description:
      "The caller gets 202 Accepted with a run id and a poll token straight away, and collects the result later.",
    icon: Hourglass,
  },
  {
    value: "sync",
    title: "Wait for answer (API)",
    description:
      "The caller gets the function's answer directly. Return { statusCode, headers, body } to control the HTTP response. If the run takes too long, the caller gets 202 + poll token as usual.",
    icon: Zap,
  },
];

/**
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
  const patch = (partial: Partial<ITriggerConfig>) => onChange({ ...value, ...partial });
  const rolesQuery = useIamRoles();
  const permissionsQuery = useIamPermissions();
  const isToken = value.authMode === "Token";
  const isSync = value.responseMode === "sync";
  const hasVerbList = (value.httpMethods ?? []).length > 0;
  const accepted = acceptedHttpVerbs(value);

  const onResponseModeChange = (mode: string) => {
    // Cookies only apply to a sync answer; clearing on the way out keeps a hidden "on" from
    // coming back the next time someone picks "Wait for answer".
    if (mode === "sync") patch({ responseMode: "sync" });
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
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <p className="text-sm font-semibold">HTTP endpoint</p>
              {isSync ? (
                <p className="text-xs text-muted-foreground">
                  Always on. The caller waits for the function&apos;s answer; a run that takes too
                  long returns <code className="font-mono">202 Accepted</code> with a run id and a
                  poll token instead.
                </p>
              ) : (
                <p className="text-xs text-muted-foreground">
                  Always on. The call returns <code className="font-mono">202 Accepted</code> with a
                  run id and a poll token — never the result itself, so nothing holds a connection
                  open for the length of a run.
                </p>
              )}
            </div>
            {/* The single-method shape every older function has. Hidden while a multi-verb list is in
                use: the verb buttons below are the only editor then, so nothing — not even moving
                keyboard focus across these tabs — can collapse the list back to one method.
                Manual activation: focus alone never changes the method. */}
            {!hasVerbList && (
            <Tabs
              value={value.httpMethod}
              activationMode="manual"
              onValueChange={(method) =>
                patch({ httpMethod: method as HttpTriggerMethod, httpMethods: [] })
              }
              className="w-auto flex-shrink-0"
            >
              <TabsList
                className="grid h-8 grid-cols-2 rounded-md bg-muted p-0.5"
                aria-label="HTTP method"
              >
                {FUNCTION_HTTP_METHODS.map((method) => (
                  <TabsTrigger
                    key={method}
                    value={method}
                    className="rounded px-3 font-mono text-xs"
                  >
                    {toHttpVerb(method)}
                  </TabsTrigger>
                ))}
              </TabsList>
            </Tabs>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs text-muted-foreground">Accepted methods</span>
            <div className="flex flex-wrap gap-1" role="group" aria-label="Accepted methods">
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
                    title={isLast ? "At least one method is required" : undefined}
                    aria-label={`Accept ${verb}`}
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
            <p className="text-sm font-semibold">Response</p>
            <p className="text-xs text-muted-foreground">
              What an HTTP caller gets back. Workflow and test runs are not affected.
            </p>
          </div>
          <RadioGroup
            value={value.responseMode}
            onValueChange={onResponseModeChange}
            className="gap-2"
            aria-label="Response"
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
            <p className="text-sm font-semibold">Who can call it</p>
            <p className="text-xs text-muted-foreground">
              One setting for every path of this function. Nothing is ever public by omission.
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
                  <span className="font-semibold">Restrict further</span>
                  <span className="text-muted-foreground"> — optional, both work together</span>
                </p>
                <Tabs
                  value={value.combine}
                  onValueChange={(combine) => patch({ combine: combine as AccessCombine })}
                  className="w-auto flex-shrink-0"
                >
                  <TabsList
                    className="grid h-8 grid-cols-2 rounded-md bg-muted p-0.5"
                    aria-label="How roles and permissions combine"
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
                label="Roles"
                noun="role"
                addLabel="Add role"
                rule={toRule(value.roles, value.roleMatch)}
                options={rolesQuery.data ?? []}
                loading={rolesQuery.isLoading}
                onChange={(rule) => {
                  const { values, match } = fromRule(rule);
                  patch({ roles: values, roleMatch: match });
                }}
              />
              <AccessRulePicker
                label="Permissions"
                noun="permission"
                addLabel="Add permission"
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
              {describeTriggerAccess(value)} <code className="font-mono">ctx.context.userId</code>{" "}
              is <code className="font-mono">null</code> and{" "}
              <code className="font-mono">isAuthenticated</code> is{" "}
              <code className="font-mono">false</code>.
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  );
};
