import { tStatic } from "@seliseblocks/genesis-os";
import { NodeGuideActionStorageV2 } from "../node-guides";
import { authClientService } from "../../services/iam.service";
import { NodeSchemaDefinition } from "./node-schema.type";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

const t = (key: string) => tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, `NODE_SCHEMA_ACTION_STORAGE_V2.${key}`);

/** Operations of the server's ActionStorageV2Node (SPEC11). */
export const STORAGE_OPERATIONS = ["get", "delete", "rename"] as const;
export const STORAGE_AUTH_BLOCKS = "blocksAuthentication";
export const STORAGE_AUTH_CLIENT_CREDENTIAL = "clientCredential";

/**
 * Storage node (SPEC11): Get, Delete or Rename one stored file per item through the blocks-data Files API.
 * A new type, so it starts at v2 (it owns its handle) with no v1.
 */
export const NodeSchemaActionStorageV2: NodeSchemaDefinition = {
  guide: NodeGuideActionStorageV2,
  schema: {
    type: "storage",
    category: "action",
    version: "v2",
    parameters: [
      {
        id: "operation",
        type: "select",
        get label() { return t("OPERATION"); },
        get info() { return t("OPERATION_INFO"); },
        key: "operation",
        required: true,
        defaultValue: "get",
        options: [
          { get label() { return t("GET"); }, value: "get" },
          { get label() { return t("DELETE"); }, value: "delete" },
          { get label() { return t("RENAME"); }, value: "rename" },
        ],
      },
      {
        id: "authenticationType",
        type: "select",
        get label() { return t("AUTHENTICATION"); },
        get info() { return t("AUTHENTICATION_INFO"); },
        key: "authenticationType",
        required: true,
        defaultValue: STORAGE_AUTH_BLOCKS,
        options: [
          { get label() { return t("BLOCKS_AUTHENTICATION"); }, value: STORAGE_AUTH_BLOCKS },
          { get label() { return t("CLIENT_CREDENTIAL"); }, value: STORAGE_AUTH_CLIENT_CREDENTIAL },
        ],
      },
      {
        id: "clientCredential",
        type: "select",
        get label() { return t("CLIENT_CREDENTIAL"); },
        get info() { return t("CLIENT_CREDENTIAL_INFO"); },
        key: "clientId",
        required: (data) => data.authenticationType === STORAGE_AUTH_CLIENT_CREDENTIAL,
        searchable: true,
        options: () =>
          authClientService.clients
            .getClientCredentials()
            .then((res) =>
              res.filter((item) => item.isActive).map((item) => ({ value: item.itemId, label: item.name })),
            ),
        dependsOn: { key: "authenticationType", value: STORAGE_AUTH_CLIENT_CREDENTIAL, operator: "equals" },
      },
      {
        id: "fileId",
        type: "text",
        get label() { return t("FILE_ID"); },
        get info() { return t("FILE_ID_INFO"); },
        key: "fileId",
        required: true,
        placeholder: "{{$json.files[0].fileId}}",
      },
      {
        id: "newName",
        type: "text",
        get label() { return t("NEW_NAME"); },
        get info() { return t("NEW_NAME_INFO"); },
        key: "newName",
        required: (data) => data.operation === "rename",
        placeholder: "invoice-{{$json.name}}",
        dependsOn: { key: "operation", value: "rename", operator: "equals" },
      },
      {
        id: "permanent",
        type: "switch",
        get label() { return t("DELETE_PERMANENTLY"); },
        get info() { return t("DELETE_PERMANENTLY_INFO"); },
        key: "permanent",
        defaultValue: false,
        dependsOn: { key: "operation", value: "delete", operator: "equals" },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      operation: "get",
      authenticationType: STORAGE_AUTH_BLOCKS,
      clientId: "",
      fileId: "",
      newName: "",
      permanent: false,
    },
    settings: {},
  },
  transform: (node) => node,
};
