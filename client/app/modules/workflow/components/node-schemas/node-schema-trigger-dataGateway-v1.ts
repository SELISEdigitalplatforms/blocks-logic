import React from "react";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { dataService } from "../../services/data.service";
import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaTriggerDataGatewayV1: NodeSchemaDefinition = {
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
          title: "Notes of editor execution",
          description: React.createElement(
            "span",
            null,
            "Editor test mode will only pickup data triggers on records that have the ",
            React.createElement(
              "code",
              { className: "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold" },
              "Tags"
            ),
            " property value of ",
            React.createElement(
              "code",
              { className: "bg-muted px-1.5 py-0.5 rounded-md text-sm font-mono text-primary font-semibold" },
              "mock-data"
            ),
            ". Whenever a data has mock data value it will be ignored in the published workflow data trigger."
          ),
        }),
      },
      {
        id: "collection",
        type: "select",
        label: "Collection",
        info: "Select the data collection to monitor for changes",
        key: "collectionName_composite",
        required: true,
        searchable: true,
        options: (_data, config) => {
          return dataService
            .getSchemaList({
              projectKey: config.projectKey,
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
          const selectedProject = useProjectStore.getState().selectedProject;
          const projectKey = selectedProject?.tenantId ?? "";

          return {
            collectionName_composite: value,
            collectionName,
            schemaName,
            projectKey,
          };
        },
      },
      {
        id: "operation",
        type: "select",
        label: "Operation",
        info: "The data operation that should trigger this workflow",
        key: "operation",
        required: true,
        options: [
          { label: "Inserted", value: "Inserted" },
          { label: "Updated", value: "Updated" },
          { label: "Deleted", value: "Deleted" },
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
        label: "Output",
        info: "Structure of the trigger output data",
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
      projectKey: "",
      operation: "",
    },
    settings: {},
  },
  transform: (node) => {
    const selectedProject = useProjectStore.getState().selectedProject;
    return {
      ...node,
      parameters: {
        ...node.parameters,
        projectKey: selectedProject?.tenantId ?? "",
      },
    };
  },
};
