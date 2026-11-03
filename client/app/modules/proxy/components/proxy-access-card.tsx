import { useMemo, useState } from "react";
import { Control, useController } from "react-hook-form";
import { Check, Globe, KeyRound, Loader2, Plus, X } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui-kits/command/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { RadioGroup, RadioGroupItem } from "@/components/ui-kits/radio-group/radio-group";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { cn } from "@/lib/utils";
import { AccessOption, useIamPermissions, useIamRoles } from "../hooks";
import {
  ProxyAccess,
  ProxyAccessCombine,
  ProxyAccessKind,
  ProxyAccessRule,
  ProxyFormValues,
} from "../types";
import {
  defaultProxyAccess,
  describeProxyAccess,
  emptyAccessRule,
  validateProxyAccess,
} from "../utils";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_PROXY_COMPONENTS_MODULE = "logic.proxy.components";

type Props = {
  control: Control<ProxyFormValues>;
};

const KIND_OPTIONS: Array<{
  value: ProxyAccessKind;
  title: string;
  description: string;
  icon: typeof KeyRound;
}> = [
  {
    value: "blocksToken",
    get title() { return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_ACCESS_CARD.BLOCKS_TOKEN"); },
    get description() { return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_ACCESS_CARD.THE_CALLER_SENDS_A"); },
    icon: KeyRound,
  },
  {
    value: "public",
    get title() { return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_ACCESS_CARD.PUBLIC"); },
    get description() { return tStatic(LOGIC_PROXY_COMPONENTS_MODULE, "PROXY_ACCESS_CARD.ANYONE_WITH_THE_URL"); },
    icon: Globe,
  },
];

/**
 * One "Restrict further" list: the chosen values as removable chips, a "+ Add …" popover listing the
 * tenant's options (typed text that matches nothing can still be added verbatim, so a key the IAM list
 * does not know yet is not a dead end), and an any/all toggle once the list has more than one entry.
 */
export const AccessRulePicker = ({
  label,
  kind,
  addLabel,
  rule,
  options,
  loading,
  showValue = true,
  onChange,
}: {
  label: string;
  kind: "role" | "permission";
  addLabel: string;
  rule: ProxyAccessRule;
  options: AccessOption[];
  loading: boolean;
  /** Show each option's key beside its label in the popover. */
  showValue?: boolean;
  onChange: (rule: ProxyAccessRule) => void;
}) => {
  const { t } = useT(LOGIC_PROXY_COMPONENTS_MODULE);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const isRole = kind === "role";
  const toggleAria = (mode: "all" | "any") => {
    if (isRole) return mode === "all" ? t("PROXY_ACCESS_CARD.TOGGLE_ALL_ROLES") : t("PROXY_ACCESS_CARD.TOGGLE_ANY_ROLES");
    return mode === "all" ? t("PROXY_ACCESS_CARD.TOGGLE_ALL_PERMISSIONS") : t("PROXY_ACCESS_CARD.TOGGLE_ANY_PERMISSIONS");
  };

  const labelFor = useMemo(() => {
    const map = new Map(options.map((option) => [option.value, option.label]));
    return (value: string) => map.get(value) ?? value;
  }, [options]);

  const add = (value: string) => {
    const trimmed = value.trim();
    if (!trimmed || rule.values.includes(trimmed)) return;
    onChange({ ...rule, values: [...rule.values, trimmed] });
    setQuery("");
  };

  const remove = (value: string) =>
    onChange({ ...rule, values: rule.values.filter((each) => each !== value) });

  const trimmedQuery = query.trim();
  const queryIsNew =
    trimmedQuery.length > 0 &&
    !rule.values.includes(trimmedQuery) &&
    !options.some((option) => option.value === trimmedQuery);

  const noMatchKey = isRole ? "PROXY_ACCESS_CARD.NO_ROLE_MATCHES" : "PROXY_ACCESS_CARD.NO_PERMISSION_MATCHES";
  const noneFoundKey = isRole ? "PROXY_ACCESS_CARD.NO_ROLES_FOUND" : "PROXY_ACCESS_CARD.NO_PERMISSIONS_FOUND";
  const emptyMessage = trimmedQuery ? t(noMatchKey, { trimmedQuery }) : t(noneFoundKey);

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          {label}
        </p>
        {rule.values.length > 1 ? (
          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            <span>{t("PROXY_ACCESS_CARD.CALLER_NEEDS")}</span>
            <button
              type="button"
              className="rounded border px-1.5 py-0.5 font-medium text-foreground hover:bg-muted"
              aria-label={toggleAria(rule.mode === "all" ? "all" : "any")}
              onClick={() => onChange({ ...rule, mode: rule.mode === "all" ? "any" : "all" })}
            >
              {rule.mode === "all" ? t("PROXY_ACCESS_CARD.MODE_ALL") : t("PROXY_ACCESS_CARD.MODE_ANY")}
            </button>
            <span>{t("PROXY_ACCESS_CARD.OF_THESE")}</span>
          </div>
        ) : null}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        {rule.values.map((value) => (
          <Badge
            key={value}
            variant="secondary"
            className="gap-1 rounded-full px-2.5 py-1 font-normal"
          >
            <span title={value}>{labelFor(value)}</span>
            <button
              type="button"
              className="rounded-full text-muted-foreground hover:text-foreground"
              aria-label={t(isRole ? "PROXY_ACCESS_CARD.REMOVE_ROLE" : "PROXY_ACCESS_CARD.REMOVE_PERMISSION", { value: labelFor(value) })}
              onClick={() => remove(value)}
            >
              <X className="h-3 w-3" />
            </button>
          </Badge>
        ))}
        <Popover open={open} onOpenChange={setOpen}>
          <PopoverTrigger asChild>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-8 gap-1.5 rounded-full border-dashed px-3 text-xs"
            >
              <Plus className="h-3.5 w-3.5" />
              {addLabel}
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-72 p-0" align="start">
            <Command shouldFilter>
              <CommandInput
                placeholder={t(isRole ? "PROXY_ACCESS_CARD.SEARCH_ROLES" : "PROXY_ACCESS_CARD.SEARCH_PERMISSIONS")}
                value={query}
                onValueChange={setQuery}
              />
              <CommandList>
                {loading ? (
                  <div className="flex items-center gap-2 px-3 py-3 text-xs text-muted-foreground">
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    {t(isRole ? "PROXY_ACCESS_CARD.LOADING_ROLES" : "PROXY_ACCESS_CARD.LOADING_PERMISSIONS")}
                  </div>
                ) : (
                  <CommandEmpty className="px-3 py-3 text-xs text-muted-foreground">
                    {emptyMessage}
                  </CommandEmpty>
                )}
                {queryIsNew ? (
                  <CommandGroup forceMount>
                    <CommandItem
                      value={`__new__${trimmedQuery}`}
                      onSelect={() => add(trimmedQuery)}
                    >
                      <Plus className="mr-2 h-3.5 w-3.5" />
                      {t("PROXY_ACCESS_CARD.USE", { trimmedQuery })}
                    </CommandItem>
                  </CommandGroup>
                ) : null}
                {options.length ? (
                  <CommandGroup>
                    {options.map((option) => {
                      const selected = rule.values.includes(option.value);
                      return (
                        <CommandItem
                          key={option.value}
                          value={`${option.label} ${option.value}`}
                          onSelect={() => (selected ? remove(option.value) : add(option.value))}
                        >
                          <Check
                            className={cn(
                              "mr-2 h-3.5 w-3.5",
                              selected ? "opacity-100" : "opacity-0",
                            )}
                          />
                          <span className="flex-1 truncate">{option.label}</span>
                          {showValue && option.label !== option.value ? (
                            <span className="ml-2 truncate font-mono text-[10px] text-muted-foreground">
                              {option.value}
                            </span>
                          ) : null}
                        </CommandItem>
                      );
                    })}
                  </CommandGroup>
                ) : null}
              </CommandList>
            </Command>
          </PopoverContent>
        </Popover>
      </div>
    </div>
  );
};

/**
 * The "Who can call it" card. `access` is a single form field (one policy per proxy — every endpoint
 * shares it, because the gateway is one catch-all route). Public hides the restriction panel and
 * clears any chips, since the server refuses a public policy that still names roles or permissions.
 */
export const ProxyAccessCard = ({ control }: Props) => {
  const { t } = useT(LOGIC_PROXY_COMPONENTS_MODULE);
  const { field, fieldState } = useController({ control, name: "access" });
  const access: ProxyAccess = field.value ?? defaultProxyAccess();
  const update = (patch: Partial<ProxyAccess>) => field.onChange({ ...access, ...patch });

  const rolesQuery = useIamRoles();
  const permissionsQuery = useIamPermissions();

  const onKindChange = (value: string) => {
    const kind = value === "public" ? "public" : "blocksToken";
    if (kind === "public") {
      update({ kind, roles: emptyAccessRule(), permissions: emptyAccessRule() });
    } else {
      update({ kind });
    }
  };

  const problem = fieldState.error?.message ?? validateProxyAccess(access);
  const isToken = access.kind === "blocksToken";

  return (
    <Card className="rounded-xl">
      <CardContent className="space-y-4 p-0">
        <div>
          <p className="text-sm font-semibold">{t("PROXY_ACCESS_CARD.WHO_CAN_CALL_IT")}</p>
          <p className="text-xs text-muted-foreground">
            {t("PROXY_ACCESS_CARD.ONE_SETTING_FOR_EVERY")}
          </p>
        </div>

        <RadioGroup value={access.kind} onValueChange={onKindChange} className="gap-2">
          {KIND_OPTIONS.map((option) => {
            const Icon = option.icon;
            const checked = access.kind === option.value;
            return (
              <label
                key={option.value}
                className={cn(
                  "flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors",
                  checked ? "border-primary bg-primary/5" : "hover:bg-muted/40",
                )}
              >
                <RadioGroupItem value={option.value} aria-label={option.title} className="mt-0.5" />
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
                <span className="font-semibold">{t("PROXY_ACCESS_CARD.RESTRICT_FURTHER")}</span>
                <span className="text-muted-foreground"> {t("PROXY_ACCESS_CARD.OPTIONAL_BOTH_WORK_TOGETHER")}</span>
              </p>
              <Tabs
                value={access.combine}
                onValueChange={(value) => update({ combine: value as ProxyAccessCombine })}
                className="w-auto flex-shrink-0"
              >
                <TabsList
                  className="grid h-8 grid-cols-2 rounded-md bg-muted p-0.5"
                  aria-label={t("PROXY_ACCESS_CARD.HOW_ROLES_AND_PERMISSIONS")}
                >
                  <TabsTrigger value="or" className="rounded px-3 text-xs">
                    OR
                  </TabsTrigger>
                  <TabsTrigger value="and" className="rounded px-3 text-xs">
                    AND
                  </TabsTrigger>
                </TabsList>
              </Tabs>
            </div>

            <AccessRulePicker
              label={t("PROXY_ACCESS_CARD.ROLES")}
              kind="role"
              addLabel={t("PROXY_ACCESS_CARD.ADD_ROLE")}
              rule={access.roles}
              options={rolesQuery.data ?? []}
              loading={rolesQuery.isLoading}
              onChange={(roles) => update({ roles })}
            />
            <AccessRulePicker
              label={t("PROXY_ACCESS_CARD.PERMISSIONS")}
              kind="permission"
              showValue={false}
              addLabel={t("PROXY_ACCESS_CARD.ADD_PERMISSION")}
              rule={access.permissions}
              options={permissionsQuery.data ?? []}
              loading={permissionsQuery.isLoading}
              onChange={(permissions) => update({ permissions })}
            />

            <p className="text-xs text-muted-foreground" data-testid="access-summary">
              {describeProxyAccess(access)}
            </p>
          </div>
        ) : (
          <p className="text-xs text-muted-foreground" data-testid="access-summary">
            {describeProxyAccess(access)}
          </p>
        )}

        {problem ? <p className="text-xs font-medium text-destructive">{problem}</p> : null}
      </CardContent>
    </Card>
  );
};
