import { NodeGuideActionNotification } from "../node-guides";
import {
  ReadonlyDetailField,
  ReadonlyDetails,
} from "../node-inspector/form-builder/form-field.types";
import { NodeSchemaDefinition } from "./node-schema.type";
import { getQueryClient } from "@/providers/query-provider";
import { notificationConfigService } from "@blocks-workflow/services/notification-config.service";
import {
  INotificationConfig,
  NotificationRecipients,
  NotificationScope,
} from "../../models/notification-config";

const CONFIG_CACHE_TTL_MS = 30_000;
const configsKey = (tenantId: string) => ["workflow", "notification-configs", tenantId];

/**
 * The project's notification configurations, shared by the picker and the details panel. Kept in the
 * app's query cache for 30 s; a failed load is not kept, so the next open tries again.
 */
const getConfigsCached = (tenantId: string): Promise<INotificationConfig[]> =>
  getQueryClient().fetchQuery({
    queryKey: configsKey(tenantId),
    queryFn: () => notificationConfigService.fetchConfigurations(),
    staleTime: CONFIG_CACHE_TTL_MS,
  });

/** Forget cached configurations. Tests call it between cases. */
export const clearNotificationConfigCache = () =>
  getQueryClient().removeQueries({ queryKey: ["workflow", "notification-configs"] });

/**
 * Each configuration seen by the picker, so its synchronous onChange can store what the form needs to
 * show the right fields. Filled whenever the options load, which happens before anything can be picked.
 */
const configByName = new Map<string, INotificationConfig>();

const RECIPIENT_LABELS: Record<NotificationRecipients, string> = {
  owner: "Event owner only",
  all: "All users",
  targeted: "Targeted users",
};

const SCOPE_LABELS: Record<NotificationScope, string> = {
  project: "Project users",
  console: "Console users",
  both: "Project & Console users",
};

const recipientsSummary = (config: INotificationConfig) =>
  config.recipients === "targeted" && config.alsoNamedUsers
    ? `${RECIPIENT_LABELS.targeted} + event owner`
    : RECIPIENT_LABELS[config.recipients];

const channelsSummary = ({ channels }: INotificationConfig) =>
  [channels.inApp && "In-app", channels.push && "Mobile push", channels.offline && "Offline"]
    .filter(Boolean)
    .join(", ") || "No channel on";

/** A text parameter; anything that is not a string (missing, or a wrong saved shape) reads as "". */
const text = (value: unknown): string => (typeof value === "string" ? value : "");

/** How the picked configuration uses the user ids the step names, from the values stored with the name. */
export type UserIdsUse = "required" | "optional" | "unused";

export const userIdsUse = (data: Record<string, unknown>): UserIdsUse => {
  if (data.Recipients === "owner" || data.Scope === "console") return "required";
  if (data.Scope === "both" || (data.Recipients === "targeted" && data.AlsoNamedUsers === true)) {
    return "optional";
  }
  return "unused";
};

/** All users and Targeted rules count only users of the send's organization. */
const usesOrganization = (data: Record<string, unknown>) =>
  data.Recipients === "all" || data.Recipients === "targeted";

/** One-filter editor: the three fields write to their own keys and keep `SubscriptionFilters` in step. */
const filterFrom = (data: Record<string, unknown>) =>
  text(data.FilterContext).trim()
    ? [
        {
          Context: text(data.FilterContext),
          ActionName: text(data.FilterActionName),
          Value: text(data.FilterValue),
        },
      ]
    : [];

const locked = (
  field: Omit<ReadonlyDetailField["field"], "key">,
  value: unknown,
): ReadonlyDetailField => ({
  field: { ...field, id: `configDetails-${field.id}`, key: `configDetails.${field.id}` },
  value,
});

/** What the picked configuration does, read live so a change in Blocks OS shows at once. */
export const buildNotificationConfigDetails = (
  config: INotificationConfig | undefined,
): ReadonlyDetails => {
  if (!config) {
    return { fields: [], message: "This configuration no longer exists in the project." };
  }
  if (config.isBuiltIn) {
    return { fields: [], message: "Built-in configurations belong to the platform; a workflow cannot send them." };
  }

  const fields = [
    locked({ id: "audience", type: "text", label: "Audience" }, SCOPE_LABELS[config.scope]),
    locked(
      {
        id: "recipients",
        type: "text",
        label: "Recipients",
        info: "Set on the configuration. Event owner: the users this step names. All users: everyone in the organization. Targeted: the users, roles and permissions picked on the configuration.",
      },
      recipientsSummary(config),
    ),
    locked(
      {
        id: "channels",
        type: "text",
        label: "Delivery channels",
        info: "In-app: live in the browser. Mobile push: phones and browsers through Firebase. Offline: kept so users who are offline see it later.",
      },
      channelsSummary(config),
    ),
  ];

  const message = detailsWarning(config);
  return { fields, ...(message ? { message } : {}) };
};

/** Why the picked configuration will not send as expected, or undefined when it will. */
const detailsWarning = (config: INotificationConfig): string | undefined => {
  if (config.isDisabled) {
    return "This configuration is inactive: the step succeeds but nothing is sent.";
  }
  const { inApp, push, offline } = config.channels;
  if (!inApp && !push && !offline) {
    return "This configuration has no delivery channel on, so the step cannot send it.";
  }
  return undefined;
};

const EXPRESSION_HINT = "Accepts expressions such as {{$json.userId}}.";

export const NodeSchemaActionNotification: NodeSchemaDefinition = {
  guide: NodeGuideActionNotification,
  schema: {
    type: "notification",
    category: "action",
    version: "v2",
    parameters: [
      {
        id: "configuration",
        type: "select-with-description",
        label: "Configuration",
        info: "A notification configuration of this project, managed in Blocks OS → Notification. It decides who receives the notification and on which channels. Built-in configurations are not listed.",
        key: "ConfigurationName",
        required: true,
        options: (_data, config) =>
          getConfigsCached(config.tenantId)
            .then((configs) =>
              configs
                .filter((c) => !c.isBuiltIn)
                .map((c) => {
                  configByName.set(c.name, c);
                  const summary = `${recipientsSummary(c)} · ${channelsSummary(c)}`;
                  return {
                    value: c.name,
                    label: c.name,
                    description: c.isDisabled ? `${summary} · inactive, the step will skip it` : summary,
                  };
                }),
            )
            // No permission to list them, or the call failed: an empty picker, explained in the guide.
            .catch(() => []),
        onChange: (value: unknown) => {
          const picked = configByName.get(text(value));
          return {
            ConfigurationName: value,
            Recipients: picked?.recipients ?? "",
            AlsoNamedUsers: picked?.alsoNamedUsers ?? false,
            Scope: picked?.scope ?? "",
          };
        },
      },
      {
        id: "configDetails",
        type: "readonly-details",
        label: "What this configuration does",
        key: "configDetails",
        // Display only: never saved, so it always shows the configuration as it is now.
        transient: true,
        hidden: (data) => !data.ConfigurationName,
        detailsDependencies: ["ConfigurationName"],
        details: async (data, config) => {
          const name = text(data.ConfigurationName);
          if (!name) return { fields: [] };
          try {
            const configs = await getConfigsCached(config.tenantId);
            return buildNotificationConfigDetails(configs.find((c) => c.name === name));
          } catch {
            return { fields: [], message: "The project's notification configurations could not be loaded." };
          }
        },
      },
      // These fields use `hidden`, not `dependsOn`: dependsOn resets a field when its parent changes,
      // and switching the configuration back and forth should keep what was typed.
      {
        id: "userIds",
        type: "expression-list",
        label: "Users the event is about",
        info: `The event owner: the user the notification is about, such as the customer of an order. ${EXPRESSION_HINT} An expression that gives a list adds every id in it.`,
        key: "UserIds",
        placeholder: "User ID or {{ expression }}",
        addButtonText: "Add user",
        required: (data) => userIdsUse(data) === "required",
        hidden: (data) => !data.ConfigurationName || userIdsUse(data) === "unused",
      },
      {
        id: "userIdsUnused",
        type: "display",
        label: "Users the event is about",
        key: "userIdsUnused",
        transient: true,
        displayValue: () =>
          "This configuration picks its own recipients, so it does not use user ids from this step.",
        hidden: (data) => !data.ConfigurationName || userIdsUse(data) !== "unused",
      },
      {
        id: "organizationId",
        type: "text",
        label: "Organization ID",
        info: `Optional. All users and Targeted rules count only users of this organization. Empty means the default organization. ${EXPRESSION_HINT}`,
        key: "OrganizationId",
        placeholder: "default",
        hidden: (data) => !usesOrganization(data),
      },
      {
        id: "payload",
        type: "json-code-editor",
        label: "Payload",
        info: "JSON sent with the notification. Mobile push text placeholders such as {{orderId}} are filled from its fields. Expressions are filled for each item: {{$json.field}} reads a field of the current item. Values inside quotes are escaped automatically. If the payload is not valid JSON after that, the step fails. Limit 64 KB.",
        key: "Payload",
        hidden: (data) => !data.ConfigurationName,
      },
      {
        id: "filterContext",
        type: "text",
        label: "Subscription filter context",
        info: `Optional. Sent with the notification so a page subscribed to this context can match it, e.g. "order". It does not choose who receives it. ${EXPRESSION_HINT}`,
        key: "FilterContext",
        hidden: (data) => !data.ConfigurationName,
        onChange: (value, data) => ({ SubscriptionFilters: filterFrom({ ...data, FilterContext: value }) }),
      },
      {
        id: "filterActionName",
        type: "text",
        label: "Subscription filter action",
        info: `Optional, e.g. "status". ${EXPRESSION_HINT}`,
        key: "FilterActionName",
        hidden: (data) => !data.ConfigurationName || !text(data.FilterContext),
        onChange: (value, data) => ({ SubscriptionFilters: filterFrom({ ...data, FilterActionName: value }) }),
      },
      {
        id: "filterValue",
        type: "text",
        label: "Subscription filter value",
        info: `Optional, e.g. {{$json.orderId}}. ${EXPRESSION_HINT}`,
        key: "FilterValue",
        hidden: (data) => !data.ConfigurationName || !text(data.FilterContext),
        onChange: (value, data) => ({ SubscriptionFilters: filterFrom({ ...data, FilterValue: value }) }),
      },
      {
        id: "responseKey",
        type: "text",
        label: "Response key",
        info: `Optional. Sent with the notification so the receiving page can match it. Empty means the configuration name. ${EXPRESSION_HINT}`,
        key: "ResponseKey",
        hidden: (data) => !data.ConfigurationName,
      },
      {
        id: "responseValue",
        type: "text",
        label: "Response value",
        info: `Optional. Empty means "Sent". ${EXPRESSION_HINT}`,
        key: "ResponseValue",
        hidden: (data) => !data.ConfigurationName,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      ConfigurationName: "",
      Recipients: "",
      AlsoNamedUsers: false,
      Scope: "",
      UserIds: [],
      OrganizationId: "",
      Payload: '{\n  "title": "",\n  "description": ""\n}',
      FilterContext: "",
      FilterActionName: "",
      FilterValue: "",
      SubscriptionFilters: [],
      ResponseKey: "",
      ResponseValue: "",
    },
    settings: {},
  },
  transform: (node) => node,
};
