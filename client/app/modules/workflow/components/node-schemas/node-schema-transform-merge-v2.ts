import { NodeGuideTransformMergeV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { deriveMergeHandle, MERGE_MAX_INPUTS, MERGE_MIN_INPUTS, mergeInputCount } from "../../utils/merge-handles";
import { tStatic } from "@seliseblocks/genesis-os";

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
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.MODE"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.APPEND_OUTPUTS_THE_ITEMS"); },
        key: "mode",
        defaultValue: "append",
        options: [{ value: "append", get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.APPEND"); } }],
      },
      {
        id: "numberInputs",
        type: "number",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.NUMBER_OF_INPUTS"); },
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
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.RUN_WHEN"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.WHETHER_MERGE_NEEDS_DATA"); },
        key: "runWhen",
        defaultValue: "all",
        options: [
          { value: "all", get label() { return tStatic("logic.workflow.nodes.schemas", "common:ALL"); }, get description() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.OUTPUT_ONLY_WHEN_EVERY"); } },
          { value: "any", get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.ANY"); }, get description() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_MERGE_V2.OUTPUT_THE_DATA_FROM"); } },
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
