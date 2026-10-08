import { NodeExecutionStatus } from "./workflow-execution-editor.util";
import { CheckCircle2, XCircle, Loader2, Clock, CircleDashed, AlertCircle, HelpCircle } from "lucide-react";

export enum WorkflowExecutionStatus {
  Init = 0,
  Queued = 1,
  Pending = 2,
  Running = 3,
  Completed = 4,
  Failed = 5,
}

/**
 * The status to show for one execution. A run stored as Completed while a node's latest attempt
 * failed is shown Failed: runs saved before the 2026-10-08 server fix could end that way (a
 * parallel branch set the run back to Running after another branch failed it). A node failed
 * and then retried by Resume counts by its latest attempt, so a resumed run that succeeded stays Completed.
 */
export const shownExecutionStatus = (
  status: number | undefined,
  nodeExecutions?: { nodeId: string; runIndex: number; status: number }[] | null,
): number | undefined => {
  if (status !== WorkflowExecutionStatus.Completed || !nodeExecutions?.length) return status;
  const latest = new Map<string, { runIndex: number; status: number }>();
  for (const row of nodeExecutions) {
    const seen = latest.get(row.nodeId);
    if (!seen || row.runIndex > seen.runIndex) latest.set(row.nodeId, row);
  }
  for (const row of latest.values()) {
    if (row.status === NodeExecutionStatus.Failed) return WorkflowExecutionStatus.Failed;
  }
  return status;
};

export const getStatusConfig = (status: number) => {
  switch (status) {
    case WorkflowExecutionStatus.Init:
      return {
        color: "bg-gray-400",
        label: "Initialized",
        textClass: "text-gray-600",
        icon: AlertCircle,
        iconClass: "h-4 w-4",
      };
    case WorkflowExecutionStatus.Queued:
      return {
        color: "bg-blue-400",
        label: "Queued",
        textClass: "text-blue-600",
        icon: CircleDashed,
        iconClass: "h-4 w-4",
      };
    case WorkflowExecutionStatus.Pending:
      return {
        color: "bg-yellow-400",
        label: "Pending",
        textClass: "text-yellow-600",
        icon: Clock,
        iconClass: "h-4 w-4",
      };
    case WorkflowExecutionStatus.Running:
      return {
        color: "bg-purple-400",
        label: "Running",
        textClass: "text-purple-600",
        icon: Loader2,
        iconClass: "h-4 w-4 animate-spin",
      };
    case WorkflowExecutionStatus.Completed:
      return {
        color: "bg-success",
        label: "Completed",
        textClass: "text-success",
        icon: CheckCircle2,
        iconClass: "h-4 w-4",
      };
    case WorkflowExecutionStatus.Failed:
      return {
        color: "bg-error",
        label: "Failed",
        textClass: "text-error",
        icon: XCircle,
        iconClass: "h-4 w-4",
      };
    default:
      return {
        color: "bg-gray-400",
        label: "Unknown",
        textClass: "text-gray-600",
        icon: HelpCircle,
        iconClass: "h-4 w-4",
      };
  }
};
