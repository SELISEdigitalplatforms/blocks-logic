import { NodeGuideActionNotification } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
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
        info: `Optional. All users and Targeted configurations count only users of this organization. Empty means the default organization. ${EXPRESSION_HINT}`,
        key: "OrganizationId",
        placeholder: "default",
        hidden: (data) => !data.ConfigurationName,
      },
      {
        id: "userIds",
        type: "expression-list",
        label: "User IDs",
        info: `Optional. The users the event is about. Needed by Event owner only and Console users configurations. ${EXPRESSION_HINT} An expression that gives a list adds every id in it.`,
        key: "UserIds",
        placeholder: "User ID or {{ expression }}",
        addButtonText: "Add user",
        hidden: (data) => !data.ConfigurationName,
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
