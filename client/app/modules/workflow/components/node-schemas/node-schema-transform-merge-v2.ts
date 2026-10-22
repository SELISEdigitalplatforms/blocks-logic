import { NodeGuideTransformMergeV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { deriveMergeHandle, MERGE_MAX_INPUTS, MERGE_MIN_INPUTS, mergeInputCount } from "../../utils/merge-handles";

const DEFAULT_PARAMETERS = { mode: "append", numberInputs: 2, runWhen: "all" };

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
        info: "2 to 10. Applies when you leave the field or press Enter. Lowering it removes the connections on the removed inputs.",
        key: "numberInputs",
        min: MERGE_MIN_INPUTS,
        max: MERGE_MAX_INPUTS,
        step: 1,
        defaultValue: 2,
        commitOn: "blur",
        // Outside 2 to 10 or a fraction becomes the number Merge uses; an empty value is left alone and means 2.
        onChange: (value) => {
          if (typeof value !== "number" || !Number.isFinite(value)) return;
          const count = mergeInputCount(value);
          if (count !== value) return { numberInputs: count };
        },
      },
      {
        id: "runWhen",
        type: "select-with-description",
        label: "Run When",
        info: "Whether Merge needs data on every connected input.",
        key: "runWhen",
        defaultValue: "all",
        options: [
          { value: "all", label: "All", description: "Output only when every connected input has data. Otherwise, output nothing." },
          { value: "any", label: "Any", description: "Output the data from every input that has some. Empty inputs are skipped." },
        ],
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
