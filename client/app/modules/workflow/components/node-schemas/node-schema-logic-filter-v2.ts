import { NodeGuideLogicFilterV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { newSwitchCondition } from "../node-inspector/form-builder/fields/switch-condition-list";
import type { FilterConditionsValue } from "../node-inspector/form-builder/fields/filter-conditions-field";

export const NodeSchemaLogicFilterV2: NodeSchemaDefinition = {
  guide: NodeGuideLogicFilterV2,
  schema: {
    type: "filter",
    category: "logic",
    version: "v2",
    parameters: [
      {
        // No label: the list has its own "Conditions" heading. The field edits two real keys, so its own
        // key is transient and only the keys returned here are saved.
        id: "filterConditions",
        type: "filter-conditions",
        key: "filterConditions",
        transient: true,
        onChange: (value) => {
          const { conditions, conditionType } = value as FilterConditionsValue;
          return { conditions, conditionType };
        },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {},
    settings: {},
  },
  // Every new Filter gets its own parameters object: AND and one blank condition.
  transform: (node) => ({ ...node, parameters: { conditionType: "and", conditions: [newSwitchCondition()] } }),
};
