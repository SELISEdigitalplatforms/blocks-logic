import { NodeGuideTransformSplitOutV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";

export const NodeSchemaTransformSplitOutV2: NodeSchemaDefinition = {
  guide: NodeGuideTransformSplitOutV2,
  schema: {
    type: "splitOut",
    category: "transform",
    version: "v2",
    parameters: [
      {
        id: "fields",
        type: "split-out-fields",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.FIELDS_TO_SPLIT_OUT"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.THE_INPUT_FIELDS_TO"); },
        key: "fields",
        required: true,
      },
      {
        id: "include",
        type: "select-with-description",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.INCLUDE"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.WHETHER_TO_COPY_ANY"); },
        key: "include",
        defaultValue: "noOtherFields",
        options: [
          { get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.NO_OTHER_FIELDS"); }, value: "noOtherFields", get description() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.EACH_NEW_ITEM_HOLDS"); } },
          { get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.ALL_OTHER_FIELDS"); }, value: "allOtherFields", get description() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.EACH_NEW_ITEM_ALSO"); } },
          { get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.SELECTED_OTHER_FIELDS"); }, value: "selectedOtherFields", get description() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.EACH_NEW_ITEM_ALSO_2"); } },
        ],
      },
      {
        id: "fieldsToInclude",
        type: "comma-list",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.FIELDS_TO_INCLUDE"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.FIELDS_OF_THE_INPUT"); },
        get placeholder() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.E_G_EMAIL_NAME"); },
        key: "fieldsToInclude",
        defaultValue: [],
        dependsOn: { key: "include", value: "selectedOtherFields" },
      },
      {
        id: "disableDotNotation",
        type: "switch",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.DISABLE_DOT_NOTATION"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SPLIT_OUT_V2.WHETHER_TO_DISALLOW_REFERENCING"); },
        key: "disableDotNotation",
        defaultValue: false,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      fields: [],
      include: "noOtherFields",
      fieldsToInclude: [],
      disableDotNotation: false,
    },
    settings: {},
  },
  transform: (node) => node,
};
