"use client";

import { FieldProps } from "../form-field.types";
import type { Condition } from "./conditions-field";
import { SwitchConditionList } from "./switch-condition-list";

/** What the Filter field reports: both top-level parameters, which the schema's onChange saves. */
export type FilterConditionsValue = { conditions: Condition[]; conditionType: unknown };

/**
 * The Filter node's conditions: Switch's condition list on its own, with no rule card. Reads and writes the
 * node's top-level `conditions` and `conditionType`. The list reports "all" | "or"; Filter saves "and" | "or"
 * (the server's enum), so only this adapter maps "all" to "and". The saved value is shown as is: the list
 * treats anything but "or" as AND.
 */
export const FilterConditionsField = ({ field, onChange, readOnly, data }: FieldProps<FilterConditionsValue>) => {
  const conditions = Array.isArray(data.conditions) ? (data.conditions as Condition[]) : [];
  const conditionType = data.conditionType;

  return (
    <SwitchConditionList
      // The schema field has no label (the list shows its own heading); the variable picker names its
      // target from the label, so give it the heading instead of the internal key.
      field={{ ...field, label: field.label || "Conditions" }}
      idPrefix={field.id}
      conditions={conditions}
      conditionType={typeof conditionType === "string" ? conditionType : ""}
      onChange={(next) => onChange({ conditions: next, conditionType })}
      onConditionTypeChange={(next) => onChange({ conditions, conditionType: next === "or" ? "or" : "and" })}
      readOnly={readOnly}
      locked={readOnly || !!field.disabled}
    />
  );
};
