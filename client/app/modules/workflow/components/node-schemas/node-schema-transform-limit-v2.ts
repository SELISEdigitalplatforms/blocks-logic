import { NodeGuideTransformLimitV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaTransformLimitV2: NodeSchemaDefinition = {
  guide: NodeGuideTransformLimitV2,
  schema: {
    type: "limit",
    category: "transform",
    version: "v2",
    parameters: [
      {
        id: "maxItems",
        type: "number",
        label: "Max Items",
        key: "maxItems",
        required: true,
        min: 1,
        max: 2147483647,
        step: 1,
        wholeNumber: true,
        commitOn: "blur",
        defaultValue: 1,
      },
      {
        id: "keep",
        type: "select",
        label: "Keep",
        key: "keep",
        defaultValue: "first",
        options: [
          { value: "first", label: "First Items" },
          { value: "last", label: "Last Items" },
        ],
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      maxItems: 1,
      keep: "first",
    },
    settings: {},
  },
  transform: (node) => node,
};
