import { NodeGuideLogicSwitchV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import {
  defaultSwitchParameters,
  deriveSwitchHandle,
  switchMode,
  type SwitchParameters,
  type SwitchRule,
} from "../../utils/switch-handles";

const isExpression = (data: Record<string, unknown>) => switchMode(data) === "expression";
const isRules = (data: Record<string, unknown>) => !isExpression(data);

const rulesOf = (data: Record<string, unknown>): SwitchRule[] =>
  Array.isArray(data.rules) ? (data.rules as SwitchRule[]).filter(Boolean) : [];

const optionsOf = (data: Record<string, unknown>): SwitchParameters["options"] =>
  (data.options as SwitchParameters["options"]) ?? defaultSwitchParameters().options;

export const NodeSchemaLogicSwitchV2: NodeSchemaDefinition = {
  guide: NodeGuideLogicSwitchV2,
  schema: {
    type: "switch",
    category: "logic",
    version: "v2",
    parameters: [
      {
        id: "mode",
        type: "switch-mode",
        label: "Mode",
        info: "Rules: each output has its own conditions. Expression: an expression returns the output number.",
        key: "mode",
        defaultValue: "rules",
      },
      {
        id: "rules",
        type: "switch-rules",
        label: "Routing rules",
        info: "Rules are checked from top to bottom. Each rule is one output.",
        key: "rules",
        hidden: isExpression,
        // A deleted rule that was the fallback target resets the fallback to None.
        onChange: (value, data) => {
          const options = optionsOf(data);
          const keys = new Set((Array.isArray(value) ? (value as SwitchRule[]) : []).map((r) => r.key));
          const fallback = options.fallbackOutput;
          if (fallback && fallback !== "none" && fallback !== "extra" && !keys.has(fallback)) {
            return { options: { ...options, fallbackOutput: "none" } };
          }
        },
      },
      {
        id: "options.fallbackOutput",
        type: "select",
        label: "Fallback output",
        info: "Where items that match no rule go.",
        key: "options.fallbackOutput",
        defaultValue: "none",
        hidden: isExpression,
        optionsDependencies: ["rules"],
        options: async (data) => [
          { value: "none", label: "None (drop the item)" },
          { value: "extra", label: "Extra output" },
          ...rulesOf(data).map((rule, index) => ({ value: rule.key, label: `Output ${rule.outputName || index}` })),
        ],
      },
      {
        id: "options.renameFallbackOutput",
        type: "text",
        label: "Rename fallback output",
        key: "options.renameFallbackOutput",
        placeholder: "Fallback",
        hidden: (data) => isExpression(data) || optionsOf(data).fallbackOutput !== "extra",
      },
      {
        id: "options.ignoreCase",
        type: "switch",
        label: "Ignore case",
        info: "Compare text (string and array conditions) without regard to upper or lower case.",
        key: "options.ignoreCase",
        defaultValue: false,
        hidden: isExpression,
      },
      {
        id: "options.allMatchingOutputs",
        type: "switch",
        label: "Send data to all matching outputs",
        info: "Off: an item goes to the first matching rule only. On: a copy goes to every matching rule.",
        key: "options.allMatchingOutputs",
        defaultValue: false,
        hidden: isExpression,
      },
      {
        id: "outputs",
        type: "switch-outputs",
        label: "Outputs",
        info: "Output i is the i-th output in this list, starting at 0.",
        key: "outputs",
        hidden: isRules,
      },
      {
        id: "output",
        type: "expression",
        label: "Output index",
        info: "0-based output number, for example {{ $json.route }}.",
        placeholder: "0-based output number",
        key: "output",
        hidden: isRules,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {},
    settings: {},
  },
  // Every new Switch gets its own parameters object with one rule and a fresh key, and its handle.
  transform: (node) => {
    const parameters = defaultSwitchParameters();
    return { ...node, parameters: { ...parameters }, handle: deriveSwitchHandle({ ...parameters }) };
  },
};
