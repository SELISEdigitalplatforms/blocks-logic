import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaLogicIfV1: NodeSchemaDefinition = {
  schema: {
    type: "if",
    category: "logic",
    version: "v1",
    parameters: [
      {
        id: "conditionType",
        type: "select",
        label: "Condition type",
        info: "Select the type of condition to evaluate.",
        key: "conditionType",
        options: [
          { value: "all", label: "All conditions (AND)" },
          { value: "any", label: "Any condition (OR)" },
        ],
        defaultValue: "all",
      },
      {
        id: "conditions",
        type: "conditions",
        label: "Conditions",
        info: "Add conditions to evaluate and determine the execution path.",
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
