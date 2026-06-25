import { API_BASES } from "@/constants/endpoint.constant";
import { NodeSchemaDefinition } from "./node-schema.type";
import { WorkflowExecutionMode } from "../../models/workflow.model";

export const NodeSchemaTriggerWebhookV1: NodeSchemaDefinition = {
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
          { label: "Production", value: String(WorkflowExecutionMode.Production) }
        ],
        displayValue: (data: Record<string, unknown>, config) => {
          const currentMode = data.executionMode !== undefined ? Number(data.executionMode) : config.executionMode;
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
          { label: "Blocks Authentication", value: "blocksAccessToken" },
        ],
        defaultValue: "none",
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
    },
    settings: {},
  },
  transform: (node) => ({
    ...node,
    parameters: {
      ...(node.parameters as Record<string, unknown>),
      path: node.id,
    },
  }),
};
