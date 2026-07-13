import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaTransformSetFieldV1: NodeSchemaDefinition = {
  schema: {
    type: "setfield",
    category: "transform",
    version: "v1",
    parameters: [
      {
        id: "mode",
        type: "select",
        label: "Mode",
        info: "The mode of setting the value",
        key: "mode",
        options: [
          { label: "Manual Mapping", value: "manual_mapping" },
          { label: "JSON", value: "json" },
        ],
      },
      {
        id: "set-fields",
        type: "key-type-value-pairs",
        label: "Set Fields",
        info: "Set fields",
        key: "manualMappingFields",
        dependsOn: {
          key: "mode",
          value: "manual_mapping",
        },
      },
      {
        id: "json-code",
        type: "code-editor",
        dependsOn: {
          key: "mode",
          value: "json",
        },
        label: "JSON Code",
        info: "Enter JSON",
        key: "jsonCode",
      },
      {
        id: "includeOtherFields",
        type: "switch",
        label: "Include Other Input Fields",
        info: "Include other input fields",
        key: "includeOtherFields",
      },
      {
        id: "otherFieldsMode",
        type: "select-with-description",
        label: "Input Fields to Include",
        info: "How to select the fields you want to include in your output items",

        key: "otherFieldsMode",
        dependsOn: {
          key: "includeOtherFields",
          value: true,
        },
        options: [
          {
            label: "All Fields",
            value: "all",
            description: "Include all fields from the input items",
          },
          {
            label: "Specific Fields",
            value: "include",
            description: "Include specific fields from the input items",
          },
          {
            label: "Exclude Fields",
            value: "exclude",
            description: "Exclude specific fields from the input items",
          },
        ],
      },
      {
        id: "include-fields",
        type: "text",
        label: "Fields to Include",
        info: "Fields to include in the output items",
        key: "includedFields",
        dependsOn: {
          key: "otherFieldsMode",
          value: "include",
        },
      },
      {
        id: "exclude-fields",
        type: "text",
        label: "Fields to Exclude",
        info: "Fields to exclude from the output items",
        key: "excludeFields",
        dependsOn: {
          key: "otherFieldsMode",
          value: "exclude",
        },
      },
    ],
    settings: [
      {
        id: "continue-on-error",
        type: "switch",
        label: "Continue on Error",
        info: "Continue workflow execution even if this node fails",
        key: "settings.continueOnError",
      },
    ],
  },
  defaults: {
    parameters: {
      includeOtherFields: false,
      otherFieldsMode: "all",
      includedFields: "",
      excludeFields: "",
      mode: "manual_mapping",
      manualMappingFields: [],
    },
    settings: {
      continueOnError: false,
    },
  },
  transform: (node) => node,
};
