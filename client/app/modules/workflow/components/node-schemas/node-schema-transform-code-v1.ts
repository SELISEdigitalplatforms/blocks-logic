import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaTransformCodeV1: NodeSchemaDefinition = {
  schema: {
    type: "code",
    category: "transform",
    version: "v1",
    parameters: [
      {
        id: "mode",
        type: "select",
        label: "Mode",
        info: "Run the script once for all items or once per item.",
        key: "mode",
        options: [
          { label: "Run Once for All Items", value: "all" },
          { label: "Run Once for Each Item", value: "each" },
        ],
      },
      {
        id: "language",
        type: "select",
        label: "Language",
        info: "Select the programming language for the script.",
        key: "language",
        options: [
          { label: "JavaScript", value: "js" },
          { label: "Python", value: "py", disabled: true },
        ],
      },
      {
        id: "script",
        type: "code-editor-v2",
        label: "Script",
        info: "Write your transformation script here. Use the provided variables to access input data.",
        key: "script",
        language: "javascript",
        height: 280,
      },
    ],
    settings: [
      {
        id: "continue-on-error",
        type: "switch",
        label: "Continue on Error",
        info: "Continue workflow execution even if this node fails.",
        key: "settings.continueOnError",
      },
    ],
  },
  defaults: {
    parameters: {
      mode: "all",
      language: "js",
      script: "",
    },
    settings: {
      continueOnError: false,
    },
  },
  transform: (node) => node,
};
