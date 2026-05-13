import { useProjectStore } from "@/store/useProjectStore";
import { configurationService } from "../../services/configuration.service";
import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaTriggerDataGatewayV1: NodeSchemaDefinition = {
  schema: {
    type: "dataGateway",
    category: "trigger",
    version: "v1",
    parameters: [
      {
        id: "collection",
        type: "select",
        label: "Collection",
        info: "Select the data collection to monitor for changes",
        key: "collectionName_composite",
        required: true,
        searchable: true,
        options: (_data, config) => {
          return configurationService
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
      {
        id: "output",
        type: "display",
        label: "Output",
        info: "Structure of the trigger output data",
        key: "output",
        displayValue: (data) => {
          console.log("DisplayValue data:", data);

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
