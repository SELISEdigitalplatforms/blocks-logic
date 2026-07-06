export const extractTemplateBodyKeys = (templateBody?: string): string[] => {
  if (!templateBody) return [];

  const keys = new Set<string>();
  const tokenRegex = /{{\s*([^{}]+?)\s*}}/g;
  let match: RegExpExecArray | null;

  while ((match = tokenRegex.exec(templateBody)) !== null) {
    const key = match[1]?.trim();
    if (key) keys.add(key);
  }

  return Array.from(keys);
};