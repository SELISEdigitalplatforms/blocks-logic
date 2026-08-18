import { FieldSchema } from "../form-field.types";
import { getValueByPath, setValueByPath } from "./path-utils";

export const isDependencySatisfied = (
  field: FieldSchema,
  data: Record<string, unknown>,
): boolean => {
  if (!field.dependsOn) return true;

  const actual = getValueByPath(data, field.dependsOn.key);
  const expected = field.dependsOn.value;
  const operator = field.dependsOn.operator ?? "equals";

  switch (operator) {
    case "equals":
      return actual === expected;
    case "notEquals":
      return actual !== expected;
    case "in":
      return Array.isArray(expected) && expected.includes(actual);
    default:
      return true;
  }
};

export const resolveDefaultValue = (field: FieldSchema, data: Record<string, unknown>): unknown => {
  return typeof field.defaultValue === "function"
    ? field.defaultValue(data)
    : (field.defaultValue ?? null);
};

export const cascadeFieldResets = (
  fields: FieldSchema[],
  changedKey: string,
  snapshot: Record<string, unknown>,
  skippedKeys: string[] = [],
): Record<string, unknown> => {
  let updated = { ...snapshot };

  for (const field of fields) {
    if (field.dependsOn?.key === changedKey && !skippedKeys.includes(field.key)) {
      updated = setValueByPath(updated, field.key, resolveDefaultValue(field, updated));
      updated = cascadeFieldResets(fields, field.key, updated, skippedKeys);
    }
  }

  return updated;
};
