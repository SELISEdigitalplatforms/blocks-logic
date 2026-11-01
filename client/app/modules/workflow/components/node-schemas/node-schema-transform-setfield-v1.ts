import { NodeGuideTransformSetFieldV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";

export const NodeSchemaTransformSetFieldV1: NodeSchemaDefinition = {
  guide: NodeGuideTransformSetFieldV1,
  schema: {
    type: "setfield",
    category: "transform",
    version: "v1",
    parameters: [
      {
        id: "mode",
        type: "select",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.MODE");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.THE_MODE_OF_SETTING",
          );
        },
        key: "mode",
        options: [
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.MANUAL_MAPPING",
              );
            },
            value: "manual_mapping",
          },
          { label: "JSON", value: "json" },
        ],
      },
      {
        id: "set-fields",
        type: "key-type-value-pairs",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.SET_FIELDS",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.SET_FIELDS_2",
          );
        },
        key: "manualMappingFields",
        dependsOn: {
          key: "mode",
          value: "manual_mapping",
        },
      },
      {
        id: "json-code",
        type: "json-code-editor",
        dependsOn: {
          key: "mode",
          value: "json",
        },
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.JSON_CODE",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.ENTER_A_JSON_OBJECT",
          );
        },
        key: "jsonCode",
      },
      {
        id: "includeOtherFields",
        type: "switch",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.INCLUDE_OTHER_INPUT_FIELDS",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.INCLUDE_OTHER_INPUT_FIELDS_2",
          );
        },
        key: "includeOtherFields",
      },
      {
        id: "otherFieldsMode",
        type: "select-with-description",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.INPUT_FIELDS_TO_INCLUDE",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.HOW_TO_SELECT_THE",
          );
        },

        key: "otherFieldsMode",
        dependsOn: {
          key: "includeOtherFields",
          value: true,
        },
        options: [
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.ALL_FIELDS",
              );
            },
            value: "all",
            get description() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.INCLUDE_ALL_FIELDS_FROM",
              );
            },
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.SPECIFIC_FIELDS",
              );
            },
            value: "include",
            get description() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.INCLUDE_SPECIFIC_FIELDS_FROM",
              );
            },
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.EXCLUDE_FIELDS",
              );
            },
            value: "exclude",
            get description() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.EXCLUDE_SPECIFIC_FIELDS_FROM",
              );
            },
          },
        ],
      },
      {
        id: "include-fields",
        type: "text",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.FIELDS_TO_INCLUDE",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.FIELDS_TO_INCLUDE_IN",
          );
        },
        key: "includedFields",
        dependsOn: {
          key: "otherFieldsMode",
          value: "include",
        },
      },
      {
        id: "exclude-fields",
        type: "text",
        get label() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.FIELDS_TO_EXCLUDE",
          );
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_SETFIELD_V1.FIELDS_TO_EXCLUDE_FROM",
          );
        },
        key: "excludeFields",
        dependsOn: {
          key: "otherFieldsMode",
          value: "exclude",
        },
      },
    ],
    // No "Continue on Error": the engine never reads node settings, so the switch had no effect.
    settings: [],
  },
  defaults: {
    parameters: {
      includeOtherFields: false,
      otherFieldsMode: "all",
      includedFields: "",
      excludeFields: "",
      mode: "manual_mapping",
      manualMappingFields: [],
    },
    settings: {},
  },
  transform: (node) => node,
};
