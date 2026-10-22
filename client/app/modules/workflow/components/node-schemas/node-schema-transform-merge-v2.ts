import { NodeGuideTransformMergeV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { deriveMergeHandle, MERGE_MAX_INPUTS, MERGE_MIN_INPUTS } from "../../utils/merge-handles";

const DEFAULT_PARAMETERS = { mode: "append", numberInputs: 2 };
const INPUT_COUNTS = Array.from({ length: MERGE_MAX_INPUTS - MERGE_MIN_INPUTS + 1 }, (_, i) => MERGE_MIN_INPUTS + i);

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
        type: "select",
        label: "Number of Inputs",
        info: "Lowering it removes the connections on the removed inputs.",
        key: "numberInputs",
        defaultValue: 2,
        options: INPUT_COUNTS.map((count) => ({ value: count, label: String(count) })),
        // The dropdown hands back text; Merge saves a number.
        onChange: (value) => {
          const count = Number(value);
          if (typeof value === "string" && Number.isFinite(count)) return { numberInputs: count };
        },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: DEFAULT_PARAMETERS,
    settings: {},
  },
  // Every new Merge gets its own parameters and the handle they derive.
  transform: (node) => {
    const parameters = { ...DEFAULT_PARAMETERS };
    return { ...node, parameters, settings: {}, handle: deriveMergeHandle(parameters) };
  },
};
