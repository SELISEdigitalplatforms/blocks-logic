import { NodeGuideTriggerErrorTriggerV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";

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
        get label() {
          return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_ERROR_TRIGGER_V2.RUN_ON_TEST_MODE");
        },
        get info() {
          return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_ERROR_TRIGGER_V2.ALSO_RUN_WHEN_A");
        },
        key: "runOnTestMode",
        defaultValue: false,
      },
      {
        id: "output",
        type: "display",
        get label() {
          return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_ERROR_TRIGGER_V2.OUTPUT");
        },
        get info() {
          return tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, "NODE_SCHEMA_TRIGGER_ERROR_TRIGGER_V2.STRUCTURE_OF_THE_TRIGGER");
        },
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
