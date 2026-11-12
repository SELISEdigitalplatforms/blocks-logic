import { NodeGuideActionNotification } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import type { AsyncFieldProps } from "../node-inspector/form-builder/form-field.types";
import { getQueryClient } from "@/providers/query-provider";
import { notificationConfigService } from "@blocks-workflow/services/notification-config.service";
import { INotificationConfig, NotificationRecipients } from "../../models/notification-config";

const CONFIG_CACHE_TTL_MS = 30_000;
const configsKey = (tenantId: string) => ["workflow", "notification-configs", tenantId];

/**
 * The project's notification configurations, for the picker. Kept in the app's query cache for 30 s; a failed
 * load is not kept, so the next open tries again.
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

const RECIPIENT_LABELS: Record<NotificationRecipients, string> = {
  owner: "Event owner only",
  all: "All users",
  targeted: "Targeted users",
};

const recipientsSummary = (config: INotificationConfig) =>
  config.recipients === "targeted" && config.alsoNamedUsers
    ? `${RECIPIENT_LABELS.targeted} + event owner`
    : RECIPIENT_LABELS[config.recipients];

const channelsSummary = ({ channels }: INotificationConfig) =>
  [channels.inApp && "In-app", channels.push && "Mobile push", channels.offline && "Offline"]
    .filter(Boolean)
    .join(", ") || "No channel on";

/** One line under each option, so the picker shows what a configuration does. Only the name is saved. */
export const configOptionDescription = (config: INotificationConfig) =>
  [
    config.isBuiltIn && "Built-in",
    recipientsSummary(config),
    channelsSummary(config),
    config.isDisabled && "inactive, the step will skip it",
  ]
    .filter(Boolean)
    .join(" · ");

const EXPRESSION_HINT = "Accepts expressions such as {{$json.userId}}.";
const LIST_HINT = "An expression that gives a list adds every id in it.";
const DEFAULT_ORG_HINT = "Empty means the default organization.";

/** The picked configuration, from the cached list; undefined when it is not listed or the list cannot be loaded. */
const findConfig = (tenantId: string, name: unknown): Promise<INotificationConfig | undefined> =>
  typeof name === "string" && name
    ? getConfigsCached(tenantId)
        .then((configs) => configs.find((c) => c.name === name))
        .catch(() => undefined)
    : Promise.resolve(undefined);

/**
 * Organization ID for the picked configuration. Project users are always matched inside the send's
 * organization; a configuration only for Console users does not use it.
 */
export const organizationIdProps = (config?: INotificationConfig): AsyncFieldProps => {
  if (!config)
    return {
      hidden: false,
      info: `Optional. Only users of this organization receive the notification. ${DEFAULT_ORG_HINT} ${EXPRESSION_HINT}`,
    };
  if (config.scope === "console") return { hidden: true };
  const who = {
    owner: "Only the users named below who belong to this organization receive it.",
    all: "Every active user of this organization receives it.",
    targeted: "The configuration's users, roles and permissions are matched inside this organization.",
  }[config.recipients];
  return { hidden: false, info: `Optional. ${who} ${DEFAULT_ORG_HINT} ${EXPRESSION_HINT}` };
};

/**
 * User IDs for the picked configuration. The configuration decides who receives it; the ids are used only
 * by "Event owner only", by "Targeted users + event owner" (added on top), and to find Console users.
 * Anywhere else the server ignores them, so the field is hidden.
 */
export const userIdsProps = (config?: INotificationConfig): AsyncFieldProps => {
  if (!config)
    return {
      hidden: false,
      required: false,
      info: `Optional. The users the event is about. Whether they receive it depends on the configuration. ${EXPRESSION_HINT} ${LIST_HINT}`,
    };

  const parts: string[] = [];
  let required = false;
  if (config.scope !== "console") {
    if (config.recipients === "owner") {
      required = true;
      parts.push("The configuration sends only to these users.");
    } else if (config.recipients === "targeted" && config.alsoNamedUsers) {
      parts.push("Added to the users the configuration picks.");
    }
  }
  if (config.scope === "console") {
    required = true;
    parts.push("The Blocks Console users to notify; only those with access to this project receive it.");
  } else if (config.scope === "both") {
    parts.push("Blocks Console users among them with access to this project receive it too.");
  }

  if (parts.length === 0) return { hidden: true };
  return { hidden: false, required, info: [...parts, EXPRESSION_HINT, LIST_HINT].join(" ") };
};

/** Loads the picked configuration and lays its rules over a field. Nothing picked: the field stays hidden. */
const fromPickedConfig =
  (rules: (config?: INotificationConfig) => AsyncFieldProps) =>
  (data: Record<string, unknown>, ctx: { tenantId: string }): Promise<AsyncFieldProps> =>
    data.ConfigurationName
      ? findConfig(ctx.tenantId, data.ConfigurationName).then(rules)
      : Promise.resolve({ hidden: true });

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
        info: "A notification configuration of this project, managed in Blocks OS → Notification. It decides who receives the notification and on which channels.",
        key: "ConfigurationName",
        required: true,
        options: (_data, config) =>
          getConfigsCached(config.tenantId)
            .then((configs) =>
              configs.map((c) => ({ value: c.name, label: c.name, description: configOptionDescription(c) })),
            )
            // No permission to list them, or the call failed: an empty picker, explained in the guide.
            .catch(() => []),
      },
      {
        id: "organizationId",
        type: "text",
        label: "Organization ID",
        info: organizationIdProps().info,
        key: "OrganizationId",
        placeholder: "default",
        hidden: (data) => !data.ConfigurationName,
        asyncProps: fromPickedConfig(organizationIdProps),
        asyncPropsDependencies: ["ConfigurationName"],
      },
      {
        id: "userIds",
        type: "expression-list",
        label: "User IDs",
        info: userIdsProps().info,
        key: "UserIds",
        placeholder: "User ID or {{ expression }}",
        addButtonText: "Add user",
        hidden: (data) => !data.ConfigurationName,
        asyncProps: fromPickedConfig(userIdsProps),
        asyncPropsDependencies: ["ConfigurationName"],
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
      {
        id: "payload",
        type: "json-code-editor",
        label: "Payload",
        info: "JSON sent with the notification. Mobile push text placeholders such as {{orderId}} are filled from its fields. Expressions are filled for each item: {{$json.field}} reads a field of the current item. Values inside quotes are escaped automatically. If the payload is not valid JSON after that, the step fails. Limit 64 KB.",
        key: "Payload",
        hidden: (data) => !data.ConfigurationName,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      ConfigurationName: "",
      OrganizationId: "",
      UserIds: [],
      ResponseKey: "",
      ResponseValue: "",
      Payload: '{\n  "title": "",\n  "description": ""\n}',
    },
    settings: {},
  },
  transform: (node) => node,
};
