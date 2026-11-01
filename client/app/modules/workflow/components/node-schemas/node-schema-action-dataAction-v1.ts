import React from "react";
import { useProjectStore, tStatic } from "@seliseblocks/genesis-os";
import { NodeGuideActionDataActionV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import type { WorkflowStore } from "@blocks-workflow/store";

// Removed useWorkflowStore
import {
  resolveSchemaFields,
  buildEmptyFieldMapping,
} from "@blocks-workflow/utils/resolve-schema-fields";
import { dataService } from "../../services/data.service";
import { getRuntimeEnv } from "@seliseblocks/genesis-os";
import { authClientService } from "../../services/iam.service";
import { renderRichText } from "@/lib/rich-text";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

export const NodeSchemaActionDataActionV1: NodeSchemaDefinition = {
  guide: NodeGuideActionDataActionV1,
  schema: {
    type: "dataAction",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "query-mode-notes",
        type: "callout-accordion-display",
        key: "queryModeNotes",
        displayValue: () => ({
          title: tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.NOTES_ON_QUERY_FIELDS"),
          description: React.createElement("span", null, ...renderRichText(tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.RAW_QUERY_IS_RECOMMENDED"), [(text: string) => React.createElement("code", { className: "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold" }, text)])),
        }),
      },
      {
        id: "rawQueryMode",
        type: "switch",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.RAW_QUERY_MODE"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.ENTER_A_RAW_GRAPHQL"); },
        key: "rawQueryMode",
      },
      {
        id: "authenticationType",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.AUTHENTICATION"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.SELECT_HOW_TO_AUTHENTICATE"); },
        key: "authenticationType",
        required: false,
        options: [
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.NONE"); }, value: "" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.CLIENT_CREDENTIAL"); }, value: "clientCredential" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.BLOCKS_AUTHENTICATION"); }, value: "blocksAuthentication" },
          // { label: "Trigger Node Cookie", value: "triggerNodeCookie" },
        ],
      },
      {
        id: "clientCredential",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.CLIENT_CREDENTIAL"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.SELECT_A_CLIENT_CREDENTIAL"); },
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
        id: "rawQuery",
        type: "graphql-code-editor",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.RAW_QUERY"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.ENTER_THE_RAW_GRAPHQL"); },
        key: "rawQuery",
        dependsOn: {
          key: "rawQueryMode",
          value: true,
          operator: "equals",
        },
      },
      {
        id: "collection",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.COLLECTION"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.SELECT_THE_DATA_COLLECTION"); },
        key: "collectionName_composite",
        required: true,
        searchable: true,
        options: (_data, _config) => {
          return dataService
            .getSchemaList({
              pageNo: 1,
              pageSize: 200,
              sortDescending: true,
              sortBy: "CreatedDate",
              keyword: "",
              schemaType: 1,
            })
            .then((res) =>
              res.data.items.map((item) => ({
                value: `${item.collectionName}:::${item.schemaName}:::${item.id}`,
                label: item.schemaName,
              })),
            );
        },
        onChange: (value: unknown, _data: unknown, config: { store?: WorkflowStore }) => {
          const parts = (value as string).split(":::");
          const collectionName = parts[0] || "";
          const schemaName = parts[1] || "";
          const schemaId = parts[2] || "";
          const selectedProject = useProjectStore.getState().selectedProject;

          // Auto-populate schemaFields from collection schema
          if (schemaId) {
            dataService
              .getSchemaDetails(schemaId)
              .then(async (res) => {
                const fields = res.data.fields ?? [];
                const schemaFields = await resolveSchemaFields(fields);
                const fieldMapping = buildEmptyFieldMapping(schemaFields);

                const store = config?.store;
                if (!store) return;
                const selectedNode = store.getState().selectedNode;
                if (selectedNode) {
                  store.getState().updateNode(selectedNode.id, {
                    parameters: {
                      ...(selectedNode.parameters as Record<string, unknown>),
                      schemaFields,
                      getFields: [],
                      fieldMapping,
                    },
                  });
                }
              })
              .catch(() => {
                /* keep existing */
              });
          }

          return {
            collectionName_composite: value,
            collectionName,
            schemaName,
            projectShortKey: selectedProject?.tenantSlug ?? "",
            filter: {},
            fieldMapping: {},
            getFields: [],
          };
        },
        hidden: (data: Record<string, unknown>) => data.rawQueryMode === true,
      },
      {
        id: "actionType",
        type: "select",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.ACTION_TYPE"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.THE_TYPE_OF_DATA"); },
        key: "actionType",
        required: true,
        options: [
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.GET_DATA"); }, value: "getData" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.INSERT_DATA"); }, value: "insertData" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.UPDATE_DATA"); }, value: "updateData" },
          { get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.DELETE_DATA"); }, value: "deleteData" },
        ],
        hidden: (data: Record<string, unknown>) => data.rawQueryMode === true,
      },
      {
        id: "filter",
        type: "key-value-pairs",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "common:FILTER"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.KEY_VALUE_FILTER_CONDITIONS"); },
        key: "filter",
        get keyLabel() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.FIELD"); },
        get valueLabel() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.VALUE"); },
        get addButtonText() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.ADD_FILTER"); },
        dependsOn: {
          key: "actionType",
          value: "insertData",
          operator: "notEquals",
        },
        hidden: (data: Record<string, unknown>) => data.rawQueryMode === true,
      },
      {
        id: "fieldMapping",
        type: "schema-fields",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.FIELD_MAPPING"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.FIELD_VALUES_FOR_INSERT"); },
        key: "fieldMapping",
        hidden: (data: Record<string, unknown>) => {
          const actionType = data.actionType as string;
          return (
            data.rawQueryMode === true ||
            (actionType !== "insertData" && actionType !== "updateData")
          );
        },
      },
      {
        id: "getFields",
        type: "schema-field-picker",
        get label() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.FIELDS"); },
        get info() { return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_ACTION_DATA_ACTION_V1.FIELDS_TO_FETCH_FROM"); },
        key: "getFields",
        dependsOn: {
          key: "actionType",
          value: "getData",
          operator: "equals",
        },
        hidden: (data: Record<string, unknown>) => data.rawQueryMode === true,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      rawQueryMode: true,
      rawQuery: "",
      actionType: "getData",
      collectionName_composite: "",
      collectionName: "",
      schemaName: "",
      projectShortKey: "",
      authenticationType: "",
      clientId: "",
      filter: {},
      fieldMapping: {},
      getFields: [],
      schemaFields: [],
      apiBaseUrl: "",
    },
    settings: {},
  },
  transform: (node) => {
    const selectedProject = useProjectStore.getState().selectedProject;
    return {
      ...node,
      parameters: {
        ...node.parameters,
        apiBaseUrl: getRuntimeEnv("BLOCKS_DATA_BASE_URL") || "",
        projectShortKey: selectedProject?.tenantSlug ?? "",
      },
    };
  },
};
