import { NodeGuideTriggerErrorTriggerV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";

export const ERROR_TRIGGER_INFO = "No parameters. Choose this workflow in another workflow's Settings → Alert.";

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
    ],
    settings: [],
  },
  defaults: {
    parameters: {},
    settings: {},
  },
  transform: (node) => node,
};
