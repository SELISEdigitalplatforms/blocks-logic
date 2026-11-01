import { NodeGuideTransformLimitV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";

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
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_LIMIT_V2.MAX_ITEMS",
          );
        },
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
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_LIMIT_V2.KEEP");
        },
        key: "keep",
        defaultValue: "first",
        options: [
          {
            value: "first",
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_LIMIT_V2.FIRST_ITEMS",
              );
            },
          },
          {
            value: "last",
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_LIMIT_V2.LAST_ITEMS",
              );
            },
          },
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
