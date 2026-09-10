import { functionService } from "@blocks-functions/services/function.service";
import { NodeGuideActionFunction } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaActionFunction: NodeSchemaDefinition = {
  guide: NodeGuideActionFunction,
  schema: {
    type: "function",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "functionId",
        type: "select",
        label: "Function",
        info: "The deployed function to invoke. Only Live functions can be selected.",
        key: "functionId",
        required: true,
        searchable: true,
        options: () =>
          functionService
            .getFunctions({ status: "Live", pageNumber: 0, pageSize: 200 })
            .then((res) => (res.data ?? []).map((fn) => ({ value: fn.id, label: fn.name }))),
      },
      {
        id: "inputMode",
        type: "select",
        label: "Input",
        info: "\"Current item\" passes this step's input through unchanged; \"Expression\" lets you build the input yourself.",
        key: "inputMode",
        options: [
          { label: "Current item", value: "item" },
          { label: "Expression", value: "expression" },
        ],
      },
      {
        id: "inputExpression",
        type: "expression",
        label: "Input expression",
        key: "inputExpression",
        dependsOn: { key: "inputMode", value: "expression" },
      },
      {
        id: "waitTimeoutSec",
        type: "number",
        label: "Wait timeout (seconds)",
        info: "Overrides how long this step waits for a result. Leave empty to use the function's own timeout plus a grace period.",
        key: "waitTimeoutSec",
        min: 1,
        max: 60,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      functionId: "",
      inputMode: "item",
      inputExpression: "",
      waitTimeoutSec: null,
    },
    settings: {},
  },
  transform: (node) => node,
};
