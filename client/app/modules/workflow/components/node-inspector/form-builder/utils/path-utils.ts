export const getValueByPath = (data: Record<string, unknown>, path: string): unknown => {
  return path.split(".").reduce<unknown>((acc, part) => {
    if (acc && typeof acc === "object" && part in acc) {
      return (acc as Record<string, unknown>)[part];
    }
    return undefined;
  }, data);
};

export const setValueByPath = (
  data: Record<string, unknown>,
  path: string,
  value: unknown,
): Record<string, unknown> => {
  const keys = path.split(".");
  const result = { ...data };
  let current: Record<string, unknown> = result;

  for (let i = 0; i < keys.length - 1; i++) {
    const key = keys[i];
    current[key] = { ...(current[key] || {}) } as Record<string, unknown>;
    current = current[key] as Record<string, unknown>;
  }

  current[keys[keys.length - 1]] = value;
  return result;
};
