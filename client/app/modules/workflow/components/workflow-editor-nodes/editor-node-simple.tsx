import { EditorNodeBase } from "./editor-node-base";
import { Position, Node } from "@xyflow/react";
import { EditorNodeTitle } from "./editor-node-title";
import { useWorkflow } from "../../hooks";
import { EditorNodeHandle, EditorNodeHandleArrow } from "./editor-node-handle";
import { getNodeDefinition } from "../node-library-panel";
import { useMemo } from "react";
import { CheckCircle2, Loader2, XCircle, AlertCircle, Clock, CircleDashed } from "lucide-react";
import { WorkflowExecutionStatus, getStatusConfig } from "../../utils/workflow-execution-list.util";
import { cn } from "@/lib/utils";

export const EditorNodeSimple = ({ id }: Node) => {
  const { getNodeById } = useWorkflow();
  const node = getNodeById(id);

  const nodeDefinition = useMemo(() => {
    if (!node) return null;
    return getNodeDefinition(node.category, node.type, node.version);
  }, [node]);

  if (!node) return null;
  if (!nodeDefinition) return null;

  const executionStatus = node.data?.executionStatus as WorkflowExecutionStatus | undefined;
  let StatusIcon = null;
  let statusConfig = null;
  if (executionStatus !== undefined) {
    statusConfig = getStatusConfig(executionStatus);
    if (executionStatus === WorkflowExecutionStatus.Completed) StatusIcon = CheckCircle2;
    else if (executionStatus === WorkflowExecutionStatus.Failed) StatusIcon = XCircle;
    else if (executionStatus === WorkflowExecutionStatus.Running) StatusIcon = Loader2;
    else if (executionStatus === WorkflowExecutionStatus.Pending) StatusIcon = Clock;
    else if (executionStatus === WorkflowExecutionStatus.Queued) StatusIcon = CircleDashed;
    else if (executionStatus === WorkflowExecutionStatus.Init) StatusIcon = AlertCircle;
  }

  return (
    <EditorNodeBase id={id}>
      <div className="absolute -left-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {nodeDefinition.handleSpec.target.map((handleId) => (
          <EditorNodeHandle
            key={handleId}
            type="target"
            position={Position.Left}
            id={handleId}
            nodeId={node.id}
          ></EditorNodeHandle>
        ))}
      </div>

      <EditorNodeTitle icon={nodeDefinition.icon} title={nodeDefinition.title} />

      <div className="absolute -right-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {nodeDefinition.handleSpec.source.map((handleId) => (
          <EditorNodeHandle
            key={handleId}
            type="source"
            position={Position.Right}
            id={handleId}
            nodeId={node.id}
            className="relative right-auto top-auto transform-none"
          >
            <EditorNodeHandleArrow />
          </EditorNodeHandle>
        ))}
      </div>
      {StatusIcon && statusConfig && (
        <div className="absolute -bottom-2 -right-2 z-10 flex h-6 w-6 items-center justify-center rounded-full bg-background shadow-sm border">
            <StatusIcon className={cn("h-4 w-4", statusConfig.textClass, executionStatus === WorkflowExecutionStatus.Running && "animate-spin")} />
        </div>
      )}
    </EditorNodeBase>
  );
};
