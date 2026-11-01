import React from "react";
import { NodeGuideTriggerDataGatewayV1 } from "../node-guides";
import { dataService } from "../../services/data.service";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";
import { renderRichText } from "@/lib/rich-text";

export const NodeSchemaTriggerDataGatewayV1: NodeSchemaDefinition = {
  guide: NodeGuideTriggerDataGatewayV1,
  schema: {
    type: "dataGateway",
    category: "trigger",
    version: "v1",
    parameters: [
      {
        id: "execution-notes",
        type: "callout-accordion-display",
        key: "executionNotes",
        displayValue: () => ({
          title: tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.NOTES_OF_EDITOR_EXECUTION",
          ),
          description: React.createElement(
            "span",
            null,
            ...renderRichText(
              tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.A_DATA_CHANGE_RUNS",
              ),
              [
                (text: string) =>
                  React.createElement(
                    "code",
                    {
                      className:
                        "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold",
                    },
                    text,
                  ),
                (text: string) =>
                  React.createElement(
                    "code",
                    {
                      className:
                        "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold",
                    },
                    text,
                  ),
              ],
            ),
          ),
        }),
      },
      {
        id: "collection",
        type: "select",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.COLLECTION",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.SELECT_THE_DATA_COLLECTION",
          );
        },
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
              schemaType: "",
            })
            .then((res) =>
              res.data.items.map((item) => ({
                value: `${item.collectionName}:::${item.schemaName}:::${item.id}`,
                label: item.schemaName,
              })),
            );
        },
        onChange: (value: unknown) => {
          const parts = (value as string).split(":::");
          const collectionName = parts[0] || "";
          const schemaName = parts[1] || "";

          return {
            collectionName_composite: value,
            collectionName,
            schemaName,
          };
        },
      },
      {
        id: "operation",
        type: "select",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.OPERATION",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.THE_DATA_OPERATION_THAT",
          );
        },
        key: "operation",
        required: true,
        options: [
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.INSERTED",
              );
            },
            value: "Inserted",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.UPDATED",
              );
            },
            value: "Updated",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.DELETED",
              );
            },
            value: "Deleted",
          },
        ],
      },
      // {
      //   id: "mock-data-info",
      //   type: "display",
      //   key: "mockDataInfo",
      //   className:
      //     "rounded-lg my-2 bg-yellow-50 border border-yellow-500 text-yellow-700 dark:bg-yellow-950/60 dark:text-yellow-300 dark:border dark:border-yellow-700 p-3",
      //   displayValue: () =>
      //     "**Note:** If `output.Tags` has the value `mock-data`, then this execution will be considered a test execution.",
      // },
      {
        id: "output",
        type: "display",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.OUTPUT",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRIGGER_DATA_GATEWAY_V1.STRUCTURE_OF_THE_TRIGGER",
          );
        },
        key: "output",
        displayValue: (data) => {
          return `\`\`\`json
{
  "Operation": "${data.operation}",
  "CollectionName": "${data.collectionName}",
  "SchemaName": "${data.schemaName}",
  "DocumentId": String,
  "Timestamp": String,
  ...documentFields
}
\`\`\`
Document fields are populated from the selected collection schema. For Update operations, an \`UpdatedFields\` array is included instead of document fields.`;
        },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      collectionName_composite: "",
      collectionName: "",
      schemaName: "",
      operation: "",
    },
    settings: {},
  },
  transform: (node) => node,
};
