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
  return setValueByKeys(data, path.split("."), value);
};

/**
 * Copies each object along `keys` and sets the leaf. Keys are written as computed properties of
 * fresh object literals, so a `__proto__` segment can never reach an object's prototype.
 */
const setValueByKeys = (
  data: Record<string, unknown>,
  keys: string[],
  value: unknown,
): Record<string, unknown> => {
  const [key, ...rest] = keys;
  if (rest.length === 0) return { ...data, [key]: value };
  const child = { ...(data[key] || {}) } as Record<string, unknown>;
  return { ...data, [key]: setValueByKeys(child, rest, value) };
};
