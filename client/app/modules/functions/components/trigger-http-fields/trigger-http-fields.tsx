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
import { SegmentedControl } from "../trigger-tab/segmented-control";
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

const AUTH_OPTIONS: Array<{ value: AuthMode; label: string }> = [
  {
    value: "Public",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.ANYONE");
    },
  },
  {
    value: "Token",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.SIGNED_IN_USERS");
    },
  },
];

const COMBINE_OPTIONS: Array<{ value: AccessCombine; label: string }> = [
  { value: "Or", label: "OR" },
  { value: "And", label: "AND" },
];

const RESPONSE_OPTIONS: Array<{ value: TriggerResponseMode; label: string }> = [
  {
    value: "sync",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.THE_RESULT");
    },
  },
  {
    value: "async",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.A_RUN_ID_RESULT_LATER");
    },
  },
  {
    value: "sse",
    get label() {
      return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.LIVE_UPDATES");
    },
  },
];

/** One plain help line per response mode. */
const RESPONSE_HELP: Record<TriggerResponseMode, string> = {
  get sync() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.RESPONSE_HELP_RESULT");
  },
  get async() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.RESPONSE_HELP_RUN_ID");
  },
  get sse() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "TRIGGER_HTTP_CARD.RESPONSE_HELP_LIVE");
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

type TriggerHttpFieldsProps = {
  value: ITriggerConfig;
  onChange: (value: ITriggerConfig) => void;
  functionId: string;
};

const SECTION_LABEL_CLASS = "text-sm font-medium";
const HELP_CLASS = "text-xs text-medium-emphasis";

/**
 * The fields of the "Web request" section: the address, "Who can call it", the allowed request
 * types and the response. The access part reads like a proxy's access card (same IAM pickers, one
 * OR/AND between the two lists, an any/all inside each).
 *
 * The shape underneath differs from a proxy's (`roles` + `roleMatch` rather than a rule object),
 * so this component translates at its edges and nothing else has to know.
 */
export const TriggerHttpFields = ({
  value,
  onChange,
  functionId,
}: Readonly<TriggerHttpFieldsProps>) => {
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  const patch = (partial: Partial<ITriggerConfig>) => onChange({ ...value, ...partial });
  const rolesQuery = useIamRoles();
  const permissionsQuery = useIamPermissions();
  const isToken = value.authMode === "Token";
  const hasVerbList = (value.httpMethods ?? []).length > 0;
  const accepted = acceptedHttpVerbs(value);
  const hasRestriction = value.roles.length > 0 || value.permissions.length > 0;

  const onKindChange = (kind: AuthMode) => {
    if (kind === "Public") {
      // Public ignores rules, and the server drops them anyway; clearing here means the page
      // shows what will be enforced rather than chips that no longer do anything.
      patch({ authMode: "Public", roles: [], permissions: [] });
    } else {
      patch({ authMode: "Token" });
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <span className={SECTION_LABEL_CLASS}>{t("TRIGGER_HTTP_CARD.ADDRESS")}</span>
        <EndpointBadge
          functionId={functionId}
          method={value.httpMethod}
          methods={hasVerbList ? accepted : undefined}
        />
      </div>

      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className={SECTION_LABEL_CLASS}>{t("TRIGGER_HTTP_CARD.WHO_CAN_CALL_IT")}</span>
          <SegmentedControl
            value={value.authMode}
            onChange={onKindChange}
            options={AUTH_OPTIONS}
            ariaLabel={t("TRIGGER_HTTP_CARD.WHO_CAN_CALL_IT")}
          />
        </div>
        <span className={HELP_CLASS} data-testid="access-summary">
          {isToken
            ? t("TRIGGER_HTTP_CARD.AUTH_HELP_SIGNED_IN")
            : t("TRIGGER_HTTP_CARD.AUTH_HELP_ANYONE")}
        </span>

        {isToken && (
          <div
            className="flex flex-col gap-4 rounded-lg border bg-muted/20 p-4"
            data-testid="access-restrictions"
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className={SECTION_LABEL_CLASS}>
                {t("TRIGGER_HTTP_CARD.ONLY_ALLOW_SOME_USERS")}
              </span>
              <SegmentedControl
                value={value.combine}
                onChange={(combine) => patch({ combine })}
                options={COMBINE_OPTIONS}
                ariaLabel={t("TRIGGER_HTTP_CARD.HOW_ROLES_AND_PERMISSIONS")}
              />
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

            <p className={HELP_CLASS} data-testid="restriction-summary">
              {hasRestriction ? describeTriggerAccess(value) : t("TRIGGER_HTTP_CARD.NO_LIMIT_YET")}
            </p>
          </div>
        )}
      </div>

      <div className="flex flex-col gap-2">
        <span className={SECTION_LABEL_CLASS}>{t("TRIGGER_HTTP_CARD.ALLOWED_REQUEST_TYPES")}</span>
        <fieldset
          className="m-0 flex min-w-0 flex-wrap gap-1 border-0 p-0"
          aria-label={t("TRIGGER_HTTP_CARD.ALLOWED_REQUEST_TYPES")}
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
        </fieldset>
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-col">
            <span className={SECTION_LABEL_CLASS}>{t("TRIGGER_HTTP_CARD.RESPONSE")}</span>
            <span className={HELP_CLASS}>{t("TRIGGER_HTTP_CARD.WHAT_THE_CALLER_GETS_BACK")}</span>
          </div>
          <SegmentedControl
            value={value.responseMode}
            onChange={(responseMode) => patch({ responseMode })}
            options={RESPONSE_OPTIONS}
            ariaLabel={t("TRIGGER_HTTP_CARD.RESPONSE")}
          />
        </div>
        <span className={HELP_CLASS} data-testid="response-help">
          {RESPONSE_HELP[value.responseMode] ?? RESPONSE_HELP.async}
        </span>
      </div>
    </div>
  );
};
