import { Globe, KeyRound } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { cn } from "@/lib/utils";
import { AccessRulePicker } from "@/modules/proxy/components/proxy-access-card";
import { useIamPermissions, useIamRoles } from "@/modules/proxy/hooks";
import type { ProxyAccessRule } from "@/modules/proxy/types";
import { FUNCTION_HTTP_METHODS, toHttpVerb } from "../../constants/endpoint.constant";
import {
  AccessCombine,
  AuthMode,
  HttpTriggerMethod,
  ITriggerConfig,
  MatchMode,
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
      "Anyone with the URL and your project key can call it. No identity, no token-scoped work.",
    icon: Globe,
  },
];

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
              <p className="text-xs text-muted-foreground">
                Always on. The call returns <code className="font-mono">202 Accepted</code> with a
                run id, or the result itself with <code className="font-mono">?wait=true</code>.
              </p>
            </div>
            {/* One method per function, like a proxy route: the other one is refused with 405. */}
            <Tabs
              value={value.httpMethod}
              onValueChange={(method) => patch({ httpMethod: method as HttpTriggerMethod })}
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
          </div>
          <EndpointBadge functionId={functionId} method={value.httpMethod} />
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
