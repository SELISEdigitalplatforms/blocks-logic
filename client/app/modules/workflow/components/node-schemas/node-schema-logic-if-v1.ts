import { NodeGuideLogicIfV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";

export const NodeSchemaLogicIfV1: NodeSchemaDefinition = {
  guide: NodeGuideLogicIfV1,
  schema: {
    type: "if",
    category: "logic",
    version: "v1",
    parameters: [
      {
        id: "conditionType",
        type: "select",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_IF_V1.CONDITION_TYPE");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_LOGIC_IF_V1.SELECT_THE_TYPE_OF",
          );
        },
        key: "conditionType",
        options: [
          {
            value: "all",
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_LOGIC_IF_V1.ALL_CONDITIONS_AND",
              );
            },
          },
          {
            value: "or",
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_LOGIC_IF_V1.ANY_CONDITION_OR",
              );
            },
          },
        ],
        defaultValue: "all",
      },
      {
        id: "conditions",
        type: "conditions",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_IF_V1.CONDITIONS");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_LOGIC_IF_V1.ADD_CONDITIONS_TO_EVALUATE",
          );
        },
        key: "conditions",
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      conditionType: "all",
      conditions: [],
    },
    settings: {},
  },
  transform: (node) => ({
    ...node,
  }),
};
