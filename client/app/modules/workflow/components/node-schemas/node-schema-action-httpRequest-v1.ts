import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaActionHttpRequestV1: NodeSchemaDefinition = {
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
        id: "useBlocksAuthorization",
        type: "switch",
        label: "Use Blocks Authorization",
        info: "Attaches a Blocks-issued bearer token to the Authorization header when one is available for this run",
        key: "useBlocksAuthorization",
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
      useBlocksAuthorization: false,
      haveBody: false,
      bodyContentType: "",
      body: "",
    },
    settings: {},
  },
  transform: (node) => ({
    ...node,
  }),
};
