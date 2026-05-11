import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaActionSetFieldV1: NodeSchemaDefinition = {
  schema: {
    type: "setfield",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "field-name",
        type: "text",
        label: "Field Name",
        info: "The name of the field to set",
        key: "parameter.fieldName",
        required: true,
        placeholder: "fieldName",
      },
      {
        id: "field-value",
        type: "text",
        label: "Field Value",
        info: "The value to set for this field",
        key: "parameter.fieldValue",
        required: true,
        placeholder: "Enter value",
      },
      {
        id: "value-type",
        type: "select",
        label: "Value Type",
        info: "The data type of the value",
        key: "parameter.valueType",
        options: [
          { label: "String", value: "string" },
          { label: "Number", value: "number" },
          { label: "Boolean", value: "boolean" },
          { label: "Object", value: "object" },
        ],
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
      fieldValue: "",
      valueType: "string",
    },
    settings: {
      continueOnError: false,
    },
  },
};
