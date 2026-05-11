export const addCopySuffix = (input: string): string => {
  const copyRegex = /(.*?)(?:\s*\(Copy(?:\s(\d+))?\))?$/;
  const match = input.match(copyRegex);
  if (!match) return `${input} (Copy 1)`;
  const baseName = match[1].trim();
  const copyNumber = match[2] ? parseInt(match[2], 10) : 0;
  return `${baseName} (Copy${copyNumber ? ` ${copyNumber + 1}` : " 1"})`;
};
