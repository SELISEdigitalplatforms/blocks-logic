import { useProjectStore } from "@/store/useProjectStore";
import { NodeSchemaDefinition } from "./node-schema.type";

import { authClientService } from "@blocks-idp/authentication/services/auth-clients.service";
import { useWorkflowStore } from "@blocks-workflow/store/workflow-store";
import {
  resolveSchemaFields,
  buildEmptyFieldMapping,
} from "@blocks-workflow/utils/resolve-schema-fields";
import { configurationService } from "../../services/configuration.service";

export const NodeSchemaActionDataActionV1: NodeSchemaDefinition = {
  schema: {
    type: "dataAction",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "actionType",
        type: "select",
        label: "Action Type",
        info: "The type of data operation to perform",
        key: "actionType",
        required: true,
        options: [
          { label: "Get Data", value: "getData" },
          { label: "Insert Data", value: "insertData" },
          { label: "Update Data", value: "updateData" },
          { label: "Delete Data", value: "deleteData" },
        ],
      },
      {
        id: "collection",
        type: "select",
        label: "Collection",
        info: "Select the data collection to operate on",
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
          const schemaId = parts[2] || "";
          const selectedProject = useProjectStore.getState().selectedProject;
          const projectKey = selectedProject?.tenantId ?? "";

          // Auto-populate schemaFields from collection schema
          if (schemaId && projectKey) {
            configurationService
              .getSchemaDetails(schemaId, projectKey)
              .then(async (res) => {
                const fields = res.data.fields ?? [];
                const schemaFields = await resolveSchemaFields(
                  fields,
                  projectKey,
                );
                const fieldMapping = buildEmptyFieldMapping(schemaFields);

                const store = useWorkflowStore.getState();
                const selectedNode = store.selectedNode;
                if (selectedNode) {
                  store.updateNode(selectedNode.id, {
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
            projectKey,
            projectShortKey: selectedProject?.tenantSlug ?? "",
            filter: {},
            fieldMapping: {},
            getFields: [],
          };
        },
      },
      {
        id: "authenticationType",
        type: "select",
        label: "Authentication",
        info: "Select how to authenticate with the Data Gateway API",
        key: "authenticationType",
        required: false,
        options: [
          { label: "Client Credential", value: "clientCredential" },
          // { label: "Trigger Node Cookie", value: "triggerNodeCookie" },
        ],
      },
      {
        id: "clientCredential",
        type: "select",
        label: "Client Credential",
        info: "Select a client credential for authenticating with the Data Gateway API",
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
        onChange: (value: unknown) => {
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
        id: "filter",
        type: "key-value-pairs",
        label: "Filter",
        info: "Key-value filter conditions for querying, updating, or deleting data",
        key: "filter",
        keyLabel: "Field",
        valueLabel: "Value",
        addButtonText: "Add Filter",
        dependsOn: {
          key: "actionType",
          value: "insertData",
          operator: "notEquals",
        },
      },
      {
        id: "fieldMapping",
        type: "schema-fields",
        label: "Field Mapping",
        info: "Field values for insert or update operations. Auto-populated from collection schema.",
        key: "fieldMapping",
        hidden: (data: Record<string, unknown>) => {
          const actionType = data.actionType as string;
          return actionType !== "insertData" && actionType !== "updateData";
        },
      },
      {
        id: "getFields",
        type: "schema-field-picker",
        label: "Fields",
        info: "Fields to fetch from the collection. Auto-populated when you select a collection. Remove fields you don't need.",
        key: "getFields",
        dependsOn: {
          key: "actionType",
          value: "getData",
          operator: "equals",
        },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      actionType: "getData",
      collectionName_composite: "",
      collectionName: "",
      schemaName: "",
      projectKey: "",
      projectShortKey: "",
      authenticationType: "",
      clientCredential_composite: "",
      clientId: "",
      clientSecret: "",
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
        apiBaseUrl: import.meta.env.BLOCKS_API_BASE_URL || "",
        projectKey: selectedProject?.tenantId ?? "",
        projectShortKey: selectedProject?.tenantSlug ?? "",
      },
    };
  },
};
