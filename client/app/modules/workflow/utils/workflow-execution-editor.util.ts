export enum NodeExecutionStatus {
  Pending = 2,
  Running = 3,
  Completed = 4,
  Failed = 5,
}

export const getStatusStyles = (status: number) => {
  switch (status) {
    case NodeExecutionStatus.Pending:
      return {
        nodeClass: "border-yellow-500",
        edgeColor: "#eab308",
        edgeClass: "stroke-yellow-500",
      };
    case NodeExecutionStatus.Running:
      return {
        nodeClass: "border-purple-500",
        edgeColor: "#a855f7",
        edgeClass: "stroke-purple-500",
      };
    case NodeExecutionStatus.Completed:
      return {
        nodeClass: "border-success",
        edgeColor: "#18c964",
        edgeClass: "stroke-success",
      };
    case NodeExecutionStatus.Failed:
      return {
        nodeClass: "border-destructive",
        edgeColor: "#ef4444",
        edgeClass: "stroke-destructive",
      };
    default:
      return {
        nodeClass: "",
        edgeColor: "#94a3b8",
        edgeClass: "",
      };
  }
};
