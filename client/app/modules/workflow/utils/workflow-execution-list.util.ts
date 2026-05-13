export enum WorkflowExecutionStatus {
  Init = 0,
  Queued = 1,
  Pending = 2,
  Running = 3,
  Completed = 4,
  Failed = 5,
}

export const getStatusConfig = (status: number) => {
  switch (status) {
    case WorkflowExecutionStatus.Init:
      return {
        color: "bg-gray-400",
        label: "Initialized",
        textClass: "text-gray-600",
      };
    case WorkflowExecutionStatus.Queued:
      return {
        color: "bg-blue-400",
        label: "Queued",
        textClass: "text-blue-600",
      };
    case WorkflowExecutionStatus.Pending:
      return {
        color: "bg-yellow-400",
        label: "Pending",
        textClass: "text-yellow-600",
      };
    case WorkflowExecutionStatus.Running:
      return {
        color: "bg-purple-400",
        label: "Running",
        textClass: "text-purple-600",
      };
    case WorkflowExecutionStatus.Completed:
      return {
        color: "bg-success",
        label: "Completed",
        textClass: "text-success",
      };
    case WorkflowExecutionStatus.Failed:
      return {
        color: "bg-error",
        label: "Failed",
        textClass: "text-error",
      };
    default:
      return {
        color: "bg-gray-400",
        label: "Unknown",
        textClass: "text-gray-600",
      };
  }
};
