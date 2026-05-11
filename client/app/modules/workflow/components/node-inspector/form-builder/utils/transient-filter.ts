import { FieldSchema } from "../form-field.types";

export const stripTransientKeys = (
  data: Record<string, unknown>,
  fields: FieldSchema[],
): Record<string, unknown> => {
  const transientKeys = fields.filter((f) => f.transient).map((f) => f.key);
  if (transientKeys.length === 0) return data;

  const persisted = { ...data };
  for (const key of transientKeys) {
    delete persisted[key];
  }
  return persisted;
};
