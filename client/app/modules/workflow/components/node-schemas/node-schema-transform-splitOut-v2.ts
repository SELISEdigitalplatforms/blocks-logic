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
        id: "fields",
        type: "split-out-fields",
        label: "Fields To Split Out",
        info: "The input fields to break out into separate items, each with an optional output field name.",
        key: "fields",
        required: true,
      },
      {
        id: "include",
        type: "select-with-description",
        label: "Include",
        info: "Whether to copy any other fields into the new items.",
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
        type: "comma-list",
        label: "Fields To Include",
        info: "Fields of the input item to copy into each new item, separated by commas.",
        placeholder: "e.g. email, name",
        key: "fieldsToInclude",
        defaultValue: [],
        dependsOn: { key: "include", value: "selectedOtherFields" },
      },
      {
        id: "disableDotNotation",
        type: "switch",
        label: "Disable Dot Notation",
        info: "Whether to disallow referencing child fields using parent.child in the field name.",
        key: "disableDotNotation",
        defaultValue: false,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      fields: [],
      include: "noOtherFields",
      fieldsToInclude: [],
      disableDotNotation: false,
    },
    settings: {},
  },
  transform: (node) => node,
};
