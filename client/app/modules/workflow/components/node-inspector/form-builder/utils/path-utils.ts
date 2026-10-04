const FORBIDDEN_PATH_KEYS = new Set(["__proto__", "prototype", "constructor"])

const isSafePathPart = (part: string): boolean => part.length > 0 && !FORBIDDEN_PATH_KEYS.has(part)

export const getValueByPath = (data: Record<string, unknown>, path: string): unknown => {
  return path.split(".").reduce<unknown>((acc, part) => {
    if (!isSafePathPart(part)) return undefined;
    if (acc && typeof acc === "object" && Object.hasOwn(acc, part)) {
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
    if (!isSafePathPart(key)) return data;
    const next = (current[key] && typeof current[key] === "object"
      ? { ...(current[key] as Record<string, unknown>) }
      : {}) as Record<string, unknown>;
    current[key] = next;
    current = next;
  }

  const last = keys.at(-1);
  if (last === undefined || !isSafePathPart(last)) return data;
  current[last] = value;
  return result;
};
