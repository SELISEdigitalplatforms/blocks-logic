import { NodeGuideLogicSwitchV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import {
  defaultSwitchParameters,
  deriveSwitchHandle,
  type SwitchParameters,
  type SwitchRule,
} from "../../utils/switch-handles";
import { tStatic } from "@seliseblocks/genesis-os";

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
        id: "rules",
        type: "switch-rules",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.ROUTING_RULES"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.RULES_ARE_CHECKED_FROM"); },
        key: "rules",
        // A deleted rule that was the fallback target resets the fallback to None.
        onChange: (value, data) => {
          const options = optionsOf(data);
          const keys = new Set((Array.isArray(value) ? (value as SwitchRule[]) : []).map((r) => r.handleKey));
          const fallback = options.fallbackOutput;
          if (fallback && fallback !== "none" && fallback !== "extra" && !keys.has(fallback)) {
            return { options: { ...options, fallbackOutput: "none" } };
          }
        },
      },
      {
        id: "options.fallbackOutput",
        type: "select",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.FALLBACK_OUTPUT"); },
        get info() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.WHERE_ITEMS_THAT_MATCH"); },
        key: "options.fallbackOutput",
        defaultValue: "none",
        optionsDependencies: ["rules"],
        options: async (data) => [
          { value: "none", label: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.NONE_DROP_THE_ITEM") },
          { value: "extra", label: tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.EXTRA_OUTPUT") },
          ...rulesOf(data).map((rule, index) => ({ value: rule.handleKey, label: rule.handleLabel || tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.OUTPUT", { index }) })),
        ],
      },
      {
        id: "options.renameFallbackOutput",
        type: "text",
        get label() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.RENAME_FALLBACK_OUTPUT"); },
        key: "options.renameFallbackOutput",
        get placeholder() { return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_LOGIC_SWITCH_V2.FALLBACK"); },
        hidden: (data) => optionsOf(data).fallbackOutput !== "extra",
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {},
    settings: {},
  },
  // Every new Switch gets its own parameters object (mode "rules", one rule with a fresh key) and its handle.
  // "mode" has no field for now: rules is the only mode; the parameter stays for modes added later.
  transform: (node) => {
    const parameters = defaultSwitchParameters();
    return { ...node, parameters: { ...parameters }, handle: deriveSwitchHandle({ ...parameters }) };
  },
};
