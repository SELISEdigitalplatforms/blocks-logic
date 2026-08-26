import { API_BASES } from "@/constants/endpoint.constant";
import { NodeGuideTriggerWebhookV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { WorkflowExecutionMode } from "../../models/workflow.model";
import { authClientService, iamService } from "../../services/iam.service";
import { ConditionalMultiselectValue } from "../node-inspector/form-builder/form-field.types";

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
        label: "Webhook URL",
        info: "Copy this URL to trigger the workflow",
        key: "executionMode",
        transient: true,
        options: [
          { label: "Test", value: String(WorkflowExecutionMode.Test) },
          { label: "Production", value: String(WorkflowExecutionMode.Production) },
        ],
        displayValue: (data: Record<string, unknown>, config) => {
          const currentMode =
            data.executionMode !== undefined ? Number(data.executionMode) : config.executionMode;
          if (currentMode === WorkflowExecutionMode.Production) {
            return `${API_BASES.LOGIC}/Workflow/webhook/${config.projectKey}/${config.workflowId}/${config.nodeId}`;
          }
          return `${API_BASES.LOGIC}/Workflow/webhook-test/${config.projectKey}/${config.workflowId}/${config.nodeId}`;
        },
        copyable: true,
      },
      {
        id: "http-method",
        type: "select",
        label: "HTTP Method",
        info: "The HTTP method that will trigger this webhook",
        key: "httpMethod",
        required: true,
        options: [
          { label: "GET", value: "GET" },
          { label: "POST", value: "POST" },
          { label: "PUT", value: "PUT" },
          { label: "PATCH", value: "PATCH" },
          { label: "DELETE", value: "DELETE" },
        ],
        disabled: true,
      },
      {
        id: "auth-type",
        type: "select",
        label: "Authentication Type",
        info: "Select the authentication method",
        key: "authType",
        options: [
          { label: "None", value: "none" },
          { label: "Blocks Authentication", value: "blocksAuthentication" },
          { label: "Blocks Authorization", value: "blocksAuthorization" },
        ],
        defaultValue: "none",
      },
      {
        id: "organization",
        type: "select",
        label: "Organization",
        info: "",
        key: "organizationId",
        defaultValue: "",
        placeholder: "Select an organization",
        dependsOn: AUTHORIZATION_DEPENDENCY,
        options: () =>
          iamService.getOrganizations({ page: 0, pageSize: 50 }).then((response) => [
            ...response.organizations.map((org) => ({
              label: org.name,
              value: org.organizationId,
            })),
          ]),
      },
      {
        id: "authorizationMode",
        type: "select",
        label: "Authorization Mode",
        info: "Choose which rule(s) the caller must satisfy. RolesOnly: only the Roles list applies. PermissionsOnly: only the Permissions list applies. RolesAndPermissions: caller must satisfy both.",
        key: "authorizationMode",
        defaultValue: "",
        dependsOn: AUTHORIZATION_DEPENDENCY,
        options: [
          { label: "Roles only", value: "RolesOnly" },
          { label: "Permissions only", value: "PermissionsOnly" },
          { label: "Roles and Permissions", value: "RolesAndPermissions" },
        ],
      },
      {
        id: "roles",
        type: "conditional-multiselect",
        label: "Roles",
        info: "Roles the caller must hold. AND = all listed roles required, OR = at least one.",
        key: "roles",
        placeholder: "Search roles...",
        dependsOn: {
          key: "authorizationMode",
          value: ["RolesOnly", "RolesAndPermissions"],
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
        label: "Permissions",
        info: "Permissions the caller must hold. Constrained by the selected roles. AND = all listed permissions required, OR = at least one.",
        key: "permissions",
        placeholder: "Search permissions...",
        dependsOn: {
          key: "authorizationMode",
          value: ["PermissionsOnly", "RolesAndPermissions"],
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
        label: "Response",
        info: "When and how to respond to the webhook",
        key: "httpResponseMode",
        required: true,
        options: [
          { label: "Immediately", value: "immediate" },
          { label: "After Last Node Completion", value: "last" },
        ],
      },
      {
        id: "http-response-data",
        type: "select",
        label: "Response Data",
        info: "What data should be returned. If it should return all items as an array or only the first item as object.",
        key: "httpResponseData",
        required: true,
        dependsOn: {
          key: "httpResponseMode",
          value: "last",
        },
        options: [
          { label: "All Entries", value: "all" },
          { label: "First Entry", value: "first" },
          { label: "No Response Body", value: "none" },
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
      organizationId: "default",
      roles: { operator: "all", values: [] },
      permissions: { operator: "any", values: [] },
      authorizationMode: "",
    },
    settings: {},
  },
  transform: (node) => {
    const params = (node.parameters as Record<string, unknown>) ?? {};
    return {
      ...node,
      parameters: {
        ...params,
        path: node.id,
      },
    };
  },
};
