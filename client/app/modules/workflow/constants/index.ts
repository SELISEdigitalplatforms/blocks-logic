export const HANDLE_LABELS: Record<string, string> = {
  "if-true": "True",
  "if-false": "False",
};

export const getHandleLabel = (handleId?: string | null) => {
  return handleId ? HANDLE_LABELS[handleId] || "" : "";
};
