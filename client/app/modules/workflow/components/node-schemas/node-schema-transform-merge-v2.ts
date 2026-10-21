import { NodeGuideTransformMergeV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { deriveMergeHandle } from "../../utils/merge-handles";

const DEFAULT_PARAMETERS = { mode: "append", numberInputs: 2 };
const DEFAULT_SETTINGS = { runWhen: "anyInputHasData", ifInputMissing: "skip" };

export const NodeSchemaTransformMergeV2: NodeSchemaDefinition = {
  guide: NodeGuideTransformMergeV2,
  schema: {
    type: "merge",
    category: "transform",
    version: "v2",
    parameters: [
      {
        id: "mode",
        type: "select",
        label: "Mode",
        info: "Append outputs the items of Input 1, then Input 2, and so on.",
        key: "mode",
        defaultValue: "append",
        options: [{ value: "append", label: "Append" }],
      },
      {
        id: "numberInputs",
        type: "number",
        label: "Number of Inputs",
        info: "Applies when you leave the field or press Enter. Lowering it removes the connections on the removed inputs.",
        key: "numberInputs",
        min: 1,
        step: 1,
        defaultValue: 2,
        commitOn: "blur",
        // Below 1 becomes 1 and fractions are floored; an empty value is left alone and means 2.
        onChange: (value) => {
          if (typeof value !== "number" || !Number.isFinite(value)) return;
          const coerced = Math.max(1, Math.floor(value));
          if (coerced !== value) return { numberInputs: coerced };
        },
      },
    ],
    settings: [
      {
        id: "runWhen",
        type: "select",
        label: "Run When",
        info: "Whether the node runs when only some inputs sent data.",
        key: "runWhen",
        defaultValue: "anyInputHasData",
        options: [
          { value: "anyInputHasData", label: "Any input has data" },
          { value: "allInputsHaveData", label: "All inputs have data" },
        ],
      },
      {
        id: "ifInputMissing",
        type: "select",
        label: "If An Input Has No Data",
        key: "ifInputMissing",
        defaultValue: "skip",
        dependsOn: { key: "runWhen", value: "allInputsHaveData" },
        options: [
          { value: "skip", label: "Skip this node (no output)" },
          { value: "fail", label: "Fail this node" },
        ],
      },
    ],
  },
  defaults: {
    parameters: DEFAULT_PARAMETERS,
    settings: DEFAULT_SETTINGS,
  },
  // Every new Merge gets its own parameters and settings, and the handle they derive.
  transform: (node) => {
    const parameters = { ...DEFAULT_PARAMETERS };
    return { ...node, parameters, settings: { ...DEFAULT_SETTINGS }, handle: deriveMergeHandle(parameters) };
  },
};
