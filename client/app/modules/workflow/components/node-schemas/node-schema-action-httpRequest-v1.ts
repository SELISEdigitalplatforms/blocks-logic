import { NodeGuideActionHttpRequestV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { authClientService } from "../../services/iam.service";

export const NodeSchemaActionHttpRequestV1: NodeSchemaDefinition = {
  guide: NodeGuideActionHttpRequestV1,
  schema: {
    type: "httpRequest",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "http-method",
        type: "select",
        label: "Method",
        info: "The request method to use",
        key: "httpMethod",
        required: true,
        options: [
          { label: "GET", value: "GET" },
          { label: "POST", value: "POST" },
          { label: "PUT", value: "PUT" },
          { label: "PATCH", value: "PATCH" },
          { label: "DELETE", value: "DELETE" },
        ],
      },
      {
        id: "url",
        type: "text",
        label: "URL",
        info: "The URL to make the request to",
        key: "url",
      },
      {
        id: "haveQueryParameters",
        type: "switch",
        label: "Send Query Parameters",
        info: "Whether the request has query params or not",
        key: "haveQueryParameters",
      },
      {
        id: "queryParameters",
        type: "key-value-pairs",
        dependsOn: {
          key: "haveQueryParameters",
          value: true,
        },
        label: "Query Parameters",
        key: "queryParameters",
        defaultValue: {},
      },
      {
        id: "haveHeaders",
        type: "switch",
        label: "Send Headers",
        info: "Whether the request has headers or not",
        key: "haveHeaders",
      },
      {
        id: "headers",
        type: "key-value-pairs",
        dependsOn: {
          key: "haveHeaders",
          value: true,
        },
        label: "Header Parameters",
        key: "headers",
      },
      {
        id: "authenticationType",
        type: "select",
        label: "Authentication",
        info: "Attaches a bearer token to the Authorization header. Blocks Authentication uses the run's delegated token; Client Credential exchanges an IAM credential.",
        key: "authenticationType",
        required: false,
        options: [
          { label: "Blocks Authentication", value: "blocksAuthentication" },
          { label: "Client Credential", value: "clientCredential" },
        ],
      },
      {
        id: "clientCredential",
        type: "select",
        label: "Client Credential",
        info: "Select a client credential for authenticating the outbound request",
        key: "clientCredential_composite",
        required: false,
        searchable: true,
        options: (_data, config) => {
          return authClientService.clients
            .getClientCredentials({ projectKey: config.projectKey })
            .then((res) =>
              res
                .filter((item) => item.isActive)
                .map((item) => ({
                  value: `${item.itemId}:::${item.clientSecret}`,
                  label: item.name,
                })),
            );
        },
        onChange: (value: unknown, _data: unknown, _config: unknown) => {
          const parts = (value as string).split(":::");
          const clientId = parts[0] || "";
          const clientSecret = parts[1] || "";
          return {
            clientCredential_composite: value,
            clientId,
            clientSecret,
          };
        },
        dependsOn: {
          key: "authenticationType",
          value: "clientCredential",
          operator: "equals",
        },
      },
      {
        id: "haveBody",
        type: "switch",
        label: "Send Body",
        info: "Whether the request has a body or not",
        key: "havebody",
      },
      {
        id: "bodyContentType",
        type: "select",
        dependsOn: {
          key: "havebody",
          value: true,
        },
        options: [{ label: "JSON", value: "json" }],
        label: "Body Content Type",
        info: "Content-Type to use to send body parameters",
        key: "bodyContentType",
      },
      {
        id: "body",
        type: "json-code-editor",
        dependsOn: {
          key: "bodyContentType",
          value: "json",
        },
        label: "Body",
        info: "Whether the request has a body or not",
        key: "body",
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      httpMethod: "GET",
      url: "",
      haveQueryParameters: false,
      queryParameters: {},
      haveHeaders: false,
      headers: {},
      authenticationType: "",
      clientCredential_composite: "",
      clientId: "",
      clientSecret: "",
      haveBody: false,
      bodyContentType: "",
      body: "",
    },
    settings: {},
  },
  transform: (node) => {
    const parameters = { ...(node.parameters ?? {}) };
    if (!parameters.authenticationType && parameters.useBlocksAuthorization === true) {
      parameters.authenticationType = "blocksAuthentication";
    }
    return { ...node, parameters };
  },
};
