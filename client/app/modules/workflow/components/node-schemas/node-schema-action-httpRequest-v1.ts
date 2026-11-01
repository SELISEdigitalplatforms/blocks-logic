import { NodeGuideActionHttpRequestV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { authClientService } from "../../services/iam.service";
import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

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
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.METHOD"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.THE_REQUEST_METHOD_TO"); },
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
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.THE_URL_TO_MAKE"); },
        key: "url",
      },
      {
        id: "haveQueryParameters",
        type: "switch",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.SEND_QUERY_PARAMETERS"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.WHETHER_THE_REQUEST_HAS"); },
        key: "haveQueryParameters",
      },
      {
        id: "queryParameters",
        type: "key-value-pairs",
        dependsOn: {
          key: "haveQueryParameters",
          value: true,
        },
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.QUERY_PARAMETERS"); },
        key: "queryParameters",
        defaultValue: {},
      },
      {
        id: "haveHeaders",
        type: "switch",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.SEND_HEADERS"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.WHETHER_THE_REQUEST_HAS_2"); },
        key: "haveHeaders",
      },
      {
        id: "headers",
        type: "key-value-pairs",
        dependsOn: {
          key: "haveHeaders",
          value: true,
        },
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.HEADER_PARAMETERS"); },
        key: "headers",
      },
      {
        id: "authenticationType",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.AUTHENTICATION"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.ATTACHES_A_BEARER_TOKEN"); },
        key: "authenticationType",
        required: false,
        options: [
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.NONE"); }, value: "" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.BLOCKS_AUTHENTICATION"); }, value: "blocksAuthentication" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.CLIENT_CREDENTIAL"); }, value: "clientCredential" },
        ],
      },
      {
        id: "clientCredential",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.CLIENT_CREDENTIAL"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.SELECT_A_CLIENT_CREDENTIAL"); },
        key: "clientId",
        required: false,
        searchable: true,
        options: (_data, _config) => {
          return authClientService.clients
            .getClientCredentials()
            .then((res) =>
              res
                .filter((item) => item.isActive)
                .map((item) => ({
                  value: item.itemId,
                  label: item.name,
                })),
            );
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
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.SEND_BODY"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.WHETHER_THE_REQUEST_HAS_3"); },
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
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.BODY_CONTENT_TYPE"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.CONTENT_TYPE_TO_USE"); },
        key: "bodyContentType",
      },
      {
        id: "body",
        type: "json-code-editor",
        dependsOn: {
          key: "bodyContentType",
          value: "json",
        },
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.BODY"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_HTTP_REQUEST_V1.JSON_BODY_SENT_WITH"); },
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
      clientId: "",
      havebody: false,
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
