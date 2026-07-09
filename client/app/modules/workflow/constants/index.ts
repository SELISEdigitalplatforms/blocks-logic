export const HANDLE_LABELS: Record<string, string> = {
  "if-true": "True",
  "if-false": "False",
};

export const getHandleLabel = (handleId?: string | null) => {
  return handleId ? HANDLE_LABELS[handleId] || "" : "";
};

export const EXECUTION_STATUS_RUNNING = "WF003"
export const EXECUTION_STATUS_COMPLETED = "WF004"
export const EXECUTION_STATUS_FAILED = "WF005"

export const TRIGGER_NODE_LISTENING_CODE = "101"