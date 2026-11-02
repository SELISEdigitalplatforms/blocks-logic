import { NodeGuideTriggerErrorTriggerV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaTriggerErrorTriggerV2: NodeSchemaDefinition = {
  guide: NodeGuideTriggerErrorTriggerV2,
  schema: {
    type: "errorTrigger",
    category: "trigger",
    version: "v2",
    parameters: [
      {
        id: "runOnTestMode",
        type: "switch",
        label: "Run on test mode",
        info: "Also run when a test run (Execute workflow) of that workflow fails, using this workflow's saved draft.",
        key: "runOnTestMode",
        defaultValue: false,
      },
      {
        id: "output",
        type: "display",
        label: "Output",
        info: "Structure of the trigger output data",
        key: "output",
        transient: true,
        displayValue: () => `\`\`\`json
{
  "execution": {
    "workflowId": String,
    "workflowName": String,
    "executionId": String,
    "mode": "Production" | "Test",
    "startedAt": String,
    "finishedAt": String,
    "error": {
      "message": String,
      "nodeId": String,
      "nodeName": String,
      "nodeType": String
    }
  }
}
\`\`\``,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: { runOnTestMode: false },
    settings: {},
  },
  transform: (node) => node,
};
