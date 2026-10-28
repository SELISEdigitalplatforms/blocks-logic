import { NodeGuideActionNotification } from "../node-guides";
import {
  ReadonlyDetailField,
  ReadonlyDetails,
} from "../node-inspector/form-builder/form-field.types";
import { NodeSchemaDefinition } from "./node-schema.type";
import { getQueryClient } from "@/providers/query-provider";
import { notificationConfigService } from "@blocks-workflow/services/notification-config.service";
import { INotificationConfig, NotificationReceiver } from "../../models/notification-config";

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
 * Receiver of each configuration seen by the picker, so its synchronous onChange can store the receiver
 * next to the name. Filled whenever the options load, which happens before anything can be picked.
 */
const receiverByName = new Map<string, NotificationReceiver>();

const RECEIVER_LABELS: Record<NotificationReceiver, string> = {
  none: "No receiver — sends to no one",
  broadcast: "Broadcast — everyone in this project",
  users: "Users — chosen users, roles or organizations",
  filter: "Filter — pages subscribed to a filter",
};

const isReceiver = (data: Record<string, unknown>, receiver: NotificationReceiver) =>
  data.NotificationType === receiver;

/** A text parameter; anything that is not a string (missing, or a wrong saved shape) reads as "". */
const text = (value: unknown): string => (typeof value === "string" ? value : "");

/** One-filter editor: the three fields write to their own keys and keep `SubscriptionFilters` in step. */
const filterFrom = (data: Record<string, unknown>) => [
  {
    Context: text(data.FilterContext),
    ActionName: text(data.FilterActionName),
    Value: text(data.FilterValue),
  },
];

const locked = (
  field: Omit<ReadonlyDetailField["field"], "key">,
  value: unknown,
): ReadonlyDetailField => ({
  field: { ...field, id: `configDetails-${field.id}`, key: `configDetails.${field.id}` },
  value,
});

/** What the picked configuration does, read live so a change on the configuration page shows at once. */
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
    locked({ id: "receiver", type: "text", label: "Receiver" }, RECEIVER_LABELS[config.receiver]),
    locked(
      {
        id: "notifyMethod",
        type: "text",
        label: "Notify method",
        info: "The event name browsers listen for. The app must listen for exactly this name.",
        copyable: true,
      },
      config.notifyMethod,
    ),
    locked(
      {
        id: "persistence",
        type: "switch",
        label: "Save to inbox",
        info: "On: also stored under the bell, so users who are offline see it later. Off: live only.",
      },
      config.enablePersistence,
    ),
  ];

  const message = detailsWarning(config);
  return { fields, ...(message ? { message } : {}) };
};

/** Why the picked configuration will not send as expected, or undefined when it will. */
const detailsWarning = (config: INotificationConfig): string | undefined => {
  if (config.isDisabled) {
    return "This configuration is switched off for the project: the step succeeds but nothing is sent.";
  }
  if (config.receiver === "none") {
    return "This configuration has no receiver, so the step cannot send it. Pick another configuration.";
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
        info: "A notification configuration of this project (Core Services → Notification). It decides who receives the notification, the event name and whether it is saved to the inbox. Built-in configurations are not listed.",
        key: "ConfigurationName",
        required: true,
        options: (_data, config) =>
          getConfigsCached(config.tenantId)
            .then((configs) =>
              configs
                .filter((c) => !c.isBuiltIn)
                .map((c) => {
                  receiverByName.set(c.name, c.receiver);
                  return {
                    value: c.name,
                    label: c.name,
                    description: c.isDisabled
                      ? `${RECEIVER_LABELS[c.receiver]} · switched off`
                      : RECEIVER_LABELS[c.receiver],
                  };
                }),
            )
            // No permission to list them, or the call failed: an empty picker, explained in the guide.
            .catch(() => []),
        onChange: (value: unknown) => ({
          ConfigurationName: value,
          NotificationType: receiverByName.get(text(value)) ?? "none",
        }),
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
      // Recipient fields use `hidden`, not `dependsOn`: dependsOn resets a field when its parent
      // changes, and switching the configuration back and forth should keep what was typed.
      {
        id: "userIds",
        type: "expression-list",
        label: "User IDs",
        info: `Users who receive it. ${EXPRESSION_HINT} An expression that gives a list adds every id in it.`,
        key: "UserIds",
        placeholder: "User ID or {{ expression }}",
        addButtonText: "Add user",
        hidden: (data) => !isReceiver(data, "users"),
      },
      {
        id: "roles",
        type: "expression-list",
        label: "Roles",
        info: `Everyone holding one of these roles receives it. ${EXPRESSION_HINT}`,
        key: "Roles",
        placeholder: "Role or {{ expression }}",
        addButtonText: "Add role",
        hidden: (data) => !isReceiver(data, "users"),
      },
      {
        id: "organizationIds",
        type: "expression-list",
        label: "Organization IDs",
        info: "Limits the roles to these organizations. With no users or roles, every member of these organizations receives it.",
        key: "OrganizationIds",
        placeholder: "Organization ID or {{ expression }}",
        addButtonText: "Add organization",
        hidden: (data) => !isReceiver(data, "users"),
      },
      {
        id: "filterContext",
        type: "text",
        label: "Filter context",
        info: `Pages subscribed to this context receive it, e.g. "order". Required for a Filter configuration. ${EXPRESSION_HINT}`,
        key: "FilterContext",
        required: (data) => isReceiver(data, "filter"),
        hidden: (data) => !isReceiver(data, "filter"),
        onChange: (value, data) => ({ SubscriptionFilters: filterFrom({ ...data, FilterContext: value }) }),
      },
      {
        id: "filterActionName",
        type: "text",
        label: "Filter action",
        info: `Optional, e.g. "status". A subscription with no action receives every action of its context. ${EXPRESSION_HINT}`,
        key: "FilterActionName",
        hidden: (data) => !isReceiver(data, "filter"),
        onChange: (value, data) => ({ SubscriptionFilters: filterFrom({ ...data, FilterActionName: value }) }),
      },
      {
        id: "filterValue",
        type: "text",
        label: "Filter value",
        info: `Optional, e.g. {{$json.orderId}}. A subscription with no value receives every value. ${EXPRESSION_HINT}`,
        key: "FilterValue",
        hidden: (data) => !isReceiver(data, "filter"),
        onChange: (value, data) => ({ SubscriptionFilters: filterFrom({ ...data, FilterValue: value }) }),
      },
      {
        id: "payload",
        type: "json-code-editor",
        label: "Payload",
        info: "JSON sent with the notification and saved in the inbox. Expressions are filled for each item: {{$json.field}} reads a field of the current item. Values inside quotes are escaped automatically. If the payload is not valid JSON after that, the step fails. Limit 64 KB.",
        key: "Payload",
        hidden: (data) => !data.ConfigurationName,
      },
      {
        id: "saveAsObject",
        type: "switch",
        label: "Store payload as an object",
        info: "Saves the payload in the inbox as an object instead of JSON text, so it can be queried. Only matters when the configuration saves to the inbox.",
        key: "SaveAsObject",
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
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      ConfigurationName: "",
      NotificationType: "",
      UserIds: [],
      Roles: [],
      OrganizationIds: [],
      FilterContext: "",
      FilterActionName: "",
      FilterValue: "",
      SubscriptionFilters: [],
      Payload: '{\n  "title": "",\n  "description": ""\n}',
      SaveAsObject: false,
      ResponseKey: "",
      ResponseValue: "",
    },
    settings: {},
  },
  transform: (node) => node,
};
