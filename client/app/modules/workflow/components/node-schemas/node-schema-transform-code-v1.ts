import { NodeGuideTransformCodeV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";

export const NodeSchemaTransformCodeV1: NodeSchemaDefinition = {
  guide: NodeGuideTransformCodeV1,
  schema: {
    type: "code",
    category: "transform",
    version: "v1",
    parameters: [
      {
        id: "mode",
        type: "select",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_CODE_V1.MODE");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_CODE_V1.RUN_THE_SCRIPT_ONCE",
          );
        },
        key: "mode",
        options: [
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_CODE_V1.RUN_ONCE_FOR_ALL",
              );
            },
            value: "all",
          },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_CODE_V1.RUN_ONCE_FOR_EACH",
              );
            },
            value: "each",
          },
        ],
      },
      {
        id: "language",
        type: "select",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_CODE_V1.LANGUAGE");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_CODE_V1.SELECT_THE_PROGRAMMING_LANGUAGE",
          );
        },
        key: "language",
        options: [
          { label: "JavaScript", value: "js" },
          {
            get label() {
              return tStatic(
                "logic.workflow.nodes.schemas",
                "NODE_SCHEMA_TRANSFORM_CODE_V1.PYTHON",
              );
            },
            value: "py",
            disabled: true,
          },
        ],
      },
      {
        id: "script",
        type: "code-editor",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_TRANSFORM_CODE_V1.SCRIPT");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_TRANSFORM_CODE_V1.WRITE_YOUR_TRANSFORMATION_SCRIPT",
          );
        },
        key: "script",
        language: "javascript",
        height: 280,
      },
    ],
    // No "Continue on Error": the engine never reads node settings, so the switch had no effect.
    settings: [],
  },
  defaults: {
    parameters: {
      mode: "all",
      language: "js",
      script: "",
    },
    settings: {},
  },
  transform: (node) => node,
  fieldReference: (p) => ({ kind: "code", mode: p.mode === "each" ? "each" : "all" }),
};
