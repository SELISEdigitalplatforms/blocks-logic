import { API_BASES } from "@/constants/endpoint.constant";
import { NodeGuideTriggerWebhookV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { WorkflowExecutionMode } from "../../models/workflow.model";
import { iamService } from "../../services/iam.service";
import { ConditionalMultiselectValue } from "../node-inspector/form-builder/form-field.types";
import { tStatic } from "@seliseblocks/genesis-os";

const AUTHORIZATION_DEPENDENCY = {
  key: "authType",
  value: "blocksAuthorization",
} as const;

type AuthBlock = {
  organizationId?: string;
  roles?: { operator?: "all" | "any"; items?: string[] };
  permissions?: { operator?: "all" | "any"; items?: string[] };
  isRolePermission?: boolean;
};

// Roles/Permissions depend on authorizationMode, which is itself only shown for blocksAuthorization.
// dependsOn takes a single condition and doesn't check whether the parent field is visible, and a
// saved node carries authorizationMode "RolesOnly" (see transform), so hide them explicitly.
const isNotAuthorization = (data: Record<string, unknown>) =>
  data.authType !== AUTHORIZATION_DEPENDENCY.value;

const getAuth = (data: Record<string, unknown>): AuthBlock =>
  (data.authorization as AuthBlock | undefined) ?? {};

const rolesAsFieldValue = (data: Record<string, unknown>): ConditionalMultiselectValue => {
  const roles = getAuth(data).roles ?? {};
  return {
    mode: roles.operator === "any" ? "or" : "and",
    values: roles.items ?? [],
  };
};

const permissionsAsFieldValue = (data: Record<string, unknown>): ConditionalMultiselectValue => {
  const permissions = getAuth(data).permissions ?? {};
  return {
    mode: permissions.operator === "any" ? "or" : "and",
    values: permissions.items ?? [],
  };
};

export const NodeSchemaTriggerWebhookV1: NodeSchemaDefinition = {
  guide: NodeGuideTriggerWebhookV1,
  schema: {
    type: "webhook",
    category: "trigger",
    version: "v1",
    parameters: [
      {
        id: "webhook-url",
        type: "tab-with-text",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.WEBHOOK_URL",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.COPY_THIS_URL_TO",
          );
        },
        key: "executionMode",
        transient: true,
        options: [
          {
            get label() {
              return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.TEST");
            },
            value: String(WorkflowExecutionMode.Test),
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.PRODUCTION",
              );
            },
            value: String(WorkflowExecutionMode.Production),
          },
        ],
        displayValue: (data: Record<string, unknown>, config) => {
          const currentMode =
            data.executionMode !== undefined ? Number(data.executionMode) : config.executionMode;
          if (currentMode === WorkflowExecutionMode.Production) {
            return `${API_BASES.LOGIC}/Workflow/webhook/${config.workflowId}/${config.nodeId}`;
          }
          return `${API_BASES.LOGIC}/Workflow/webhook-test/${config.workflowId}/${config.nodeId}`;
        },
        copyable: true,
      },
      {
        id: "webhook-url-deprecated",
        type: "tab-with-text",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.DEPRECATED_WEBHOOK_URL",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.LEGACY_URL_WITH_THE",
          );
        },
        key: "executionMode",
        transient: true,
        options: [
          {
            get label() {
              return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.TEST");
            },
            value: String(WorkflowExecutionMode.Test),
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.PRODUCTION",
              );
            },
            value: String(WorkflowExecutionMode.Production),
          },
        ],
        displayValue: (data: Record<string, unknown>, config) => {
          const currentMode =
            data.executionMode !== undefined ? Number(data.executionMode) : config.executionMode;
          if (currentMode === WorkflowExecutionMode.Production) {
            return `${API_BASES.LOGIC}/Workflow/webhook/${config.tenantId}/${config.workflowId}/${config.nodeId}`;
          }
          return `${API_BASES.LOGIC}/Workflow/webhook-test/${config.tenantId}/${config.workflowId}/${config.nodeId}`;
        },
        copyable: true,
      },
      {
        // Webhooks accept POST only (every webhook route on the server is [HttpPost]); the old method
        // select had no effect. A saved `httpMethod` is left as it is, so older workflows load and save
        // unchanged. Display-only and transient, so nothing new is written to the node.
        id: "http-method",
        type: "display",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.HTTP_METHOD",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.WEBHOOKS_ACCEPT_POST_REQUESTS",
          );
        },
        key: "httpMethodInfo",
        transient: true,
        displayValue: () => "POST",
      },
      {
        id: "auth-type",
        type: "select",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.AUTHENTICATION_TYPE",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.SELECT_THE_AUTHENTICATION_METHOD",
          );
        },
        key: "authType",
        options: [
          {
            get label() {
              return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.NONE");
            },
            value: "none",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.BLOCKS_AUTHENTICATION",
              );
            },
            value: "blocksAuthentication",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.BLOCKS_AUTHORIZATION",
              );
            },
            value: "blocksAuthorization",
          },
        ],
        defaultValue: "none",
      },
      {
        id: "organization",
        type: "select",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.ORGANIZATION",
          );
        },
        info: "",
        key: "organizationId",
        defaultValue: "",
        get placeholder() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.SELECT_AN_ORGANIZATION",
          );
        },
        dependsOn: AUTHORIZATION_DEPENDENCY,
        options: () =>
          iamService
            .getOrganizations({ page: 0, pageSize: 50 })
            .then((response) => {
              const orgs = response.organizations ?? [];
              if (orgs.length === 0) {
                return [
                  {
                    label: tStatic(
                      "logic.workflow.nodes.schemas",
                      "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.NONE",
                    ),
                    value: "",
                  },
                ];
              }
              return [
                {
                  label: tStatic(
                    "logic.workflow.nodes.schemas",
                    "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.NONE",
                  ),
                  value: "",
                },
                {
                  label: tStatic(
                    "logic.workflow.nodes.schemas",
                    "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.DEFAULT",
                  ),
                  value: "default",
                },
                ...orgs.map((org) => ({
                  label: org.name,
                  value: org.itemId,
                })),
              ];
            })
            .catch(() => [
              {
                label: tStatic(
                  "logic.workflow.nodes.schemas",
                  "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.NONE",
                ),
                value: "",
              },
            ]),
      },
      {
        id: "authorizationMode",
        type: "select",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.AUTHORIZATION_MODE",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.CHOOSE_WHICH_RULE_S",
          );
        },
        key: "authorizationMode",
        defaultValue: "",
        dependsOn: AUTHORIZATION_DEPENDENCY,
        options: [
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.ROLES_ONLY",
              );
            },
            value: "RolesOnly",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.PERMISSIONS_ONLY",
              );
            },
            value: "PermissionsOnly",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.ROLES_AND_PERMISSIONS",
              );
            },
            value: "RolesAndPermissions",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.ROLES_OR_PERMISSIONS",
              );
            },
            value: "RolesOrPermissions",
          },
        ],
      },
      {
        id: "roles",
        type: "conditional-multiselect",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.ROLES");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.ROLES_THE_CALLER_MUST",
          );
        },
        key: "roles",
        hidden: isNotAuthorization,
        get placeholder() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.SEARCH_ROLES",
          );
        },
        dependsOn: {
          key: "authorizationMode",
          value: ["RolesOnly", "RolesAndPermissions", "RolesOrPermissions"],
          operator: "in",
        },
        defaultValue: (data: Record<string, unknown>) => rolesAsFieldValue(data),
        options: () => {
          return iamService
            .getRoles()
            .then((res) => res.data.map((role) => ({ label: role.name, value: role.slug })));
        },
      },
      {
        id: "permissions",
        type: "conditional-multiselect",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.PERMISSIONS",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.PERMISSIONS_THE_CALLER_MUST",
          );
        },
        key: "permissions",
        hidden: isNotAuthorization,
        get placeholder() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.SEARCH_PERMISSIONS",
          );
        },
        dependsOn: {
          key: "authorizationMode",
          value: ["PermissionsOnly", "RolesAndPermissions", "RolesOrPermissions"],
          operator: "in",
        },
        defaultValue: (data: Record<string, unknown>) => permissionsAsFieldValue(data),
        options: () => {
          return iamService.getPermissions({}).then((res) =>
            res.data.map((permission) => ({
              label: permission.name,
              value: permission.resource,
            })),
          );
        },
      },
      {
        id: "http-response-mode",
        type: "select",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.RESPONSE");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.WHEN_AND_HOW_TO",
          );
        },
        key: "httpResponseMode",
        required: true,
        options: [
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.IMMEDIATELY",
              );
            },
            value: "immediate",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.AFTER_LAST_NODE_COMPLETION",
              );
            },
            value: "last",
          },
        ],
      },
      {
        id: "http-response-data",
        type: "select",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.RESPONSE_DATA",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.WHAT_DATA_SHOULD_BE",
          );
        },
        key: "httpResponseData",
        required: true,
        dependsOn: {
          key: "httpResponseMode",
          value: "last",
        },
        options: [
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.ALL_ENTRIES",
              );
            },
            value: "all",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.FIRST_ENTRY",
              );
            },
            value: "first",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_WEBHOOK_V1.NO_RESPONSE_BODY",
              );
            },
            value: "none",
          },
        ],
        defaultValue: "all",
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      httpMethod: "POST",
      authType: "none",
      httpResponseMode: "immediate",
      httpResponseData: "all",
      organizationId: "",
      roles: { operator: "all", values: [] },
      permissions: { operator: "any", values: [] },
      authorizationMode: "",
    },
    settings: {},
  },
  transform: (node) => {
    const params = (node.parameters as Record<string, unknown>) ?? {};
    const mode = typeof params.authorizationMode === "string" ? params.authorizationMode : "";

    return {
      ...node,
      parameters: {
        ...params,
        path: node.id,
        // The server parses this into WorkflowAuthService.AuthorizationMode and treats an absent or
        // unparseable value as "no authorization config", which makes the trigger throw
        // UnauthorizedAccessException on every call. The node defaults this to "", so a webhook set to
        // `blocksAuthorization` with roles picked but no mode chosen would reject all traffic. Normalising
        // to the server's own default (RolesOnly = 0) here keeps the wire value always valid.
        authorizationMode: mode || "RolesOnly",
      },
    };
  },
};
