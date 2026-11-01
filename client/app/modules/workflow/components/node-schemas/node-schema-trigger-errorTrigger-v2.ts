import { NodeGuideTriggerErrorTriggerV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";

export const ERROR_TRIGGER_INFO = "Choose this workflow in another workflow's Settings → Alert.";

export const NodeSchemaTriggerErrorTriggerV2: NodeSchemaDefinition = {
  guide: NodeGuideTriggerErrorTriggerV2,
  schema: {
    type: "errorTrigger",
    category: "trigger",
    version: "v2",
    parameters: [
      {
        id: "errorTriggerInfo",
        type: "display",
        label: "",
        key: "errorTriggerInfo",
        transient: true,
        displayValue: () => ERROR_TRIGGER_INFO,
      },
      {
        id: "runOnTestMode",
        type: "switch",
        label: "Run on test mode",
        info: "Also run when a test run (Execute workflow) of that workflow fails, using this workflow's saved draft.",
        key: "runOnTestMode",
        defaultValue: false,
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
