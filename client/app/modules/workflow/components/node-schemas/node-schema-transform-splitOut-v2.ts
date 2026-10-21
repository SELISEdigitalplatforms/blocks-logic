import { NodeGuideTransformSplitOutV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaTransformSplitOutV2: NodeSchemaDefinition = {
  guide: NodeGuideTransformSplitOutV2,
  schema: {
    type: "splitOut",
    category: "transform",
    version: "v2",
    parameters: [
      {
        id: "fieldToSplitOut",
        type: "text",
        label: "Fields To Split Out",
        info: "The field that holds the list, for example lines or order.lines. Separate several fields with commas.",
        placeholder: "lines",
        key: "fieldToSplitOut",
        required: true,
      },
      {
        id: "include",
        type: "select-with-description",
        label: "Include",
        info: "Which other fields of the input item each new item keeps.",
        key: "include",
        defaultValue: "noOtherFields",
        options: [
          { label: "No other fields", value: "noOtherFields", description: "Each new item holds only the split value" },
          { label: "All other fields", value: "allOtherFields", description: "Each new item also keeps every other field of the input item" },
          { label: "Selected other fields", value: "selectedOtherFields", description: "Each new item also keeps the fields you list" },
        ],
      },
      {
        id: "fieldsToInclude",
        type: "text",
        label: "Fields To Include",
        info: "Fields of the input item to copy into each new item, separated by commas.",
        key: "fieldsToInclude",
        defaultValue: "",
        dependsOn: { key: "include", value: "selectedOtherFields" },
      },
      {
        id: "destinationFieldName",
        type: "text",
        label: "Destination Field Name",
        info: "Optional. A name to store each element under, one per field to split out, separated by commas.",
        key: "destinationFieldName",
      },
      {
        id: "disableDotNotation",
        type: "switch",
        label: "Disable Dot Notation",
        info: "Treat a.b as one key named \"a.b\" instead of the field b inside a.",
        key: "disableDotNotation",
        defaultValue: false,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      fieldToSplitOut: "",
      include: "noOtherFields",
      fieldsToInclude: "",
      destinationFieldName: "",
      disableDotNotation: false,
    },
    settings: {},
  },
  transform: (node) => node,
};
