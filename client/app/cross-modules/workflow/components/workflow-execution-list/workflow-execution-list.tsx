"use client";

import { WorkflowExecution } from "@blocks-workflow/types/workflow.service.type";
import { ScrollArea } from "@/components/ui-kits/scroll-area/scroll-area";
import { formatDate, cn } from "@/lib/utils";
import { differenceInSeconds } from "date-fns";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { getStatusConfig, WorkflowExecutionStatus } from "../../utils/workflow-execution-list.util";

interface WorkflowExecutionListProps {
  executions: WorkflowExecution[];
  isLoading?: boolean;
  selectedExecutionId?: string;
  onSelectExecution?: (execution: WorkflowExecution) => void;
}

const LoadingSkeleton = () => {
  return (
    <ScrollArea className="h-full min-w-72 border-r bg-background p-4">
      {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]?.map((execution) => (
        <div
          key={execution}
          className={`flex cursor-pointer shadow-sm`}
          // onClick={() => onSelectExecution?.(execution)}
        >
          <div className="w-1 bg-accent"></div>
          <div className="flex w-full flex-col gap-1 p-4">
            <Skeleton className="h-6 w-full" />
            <Skeleton className="mt-1 h-4 w-1/2" />
          </div>
        </div>
      ))}
    </ScrollArea>
  );
};

export const WorkflowExecutionList = ({
  executions,
  isLoading,
  onSelectExecution,
}: WorkflowExecutionListProps) => {
  if (isLoading) return <LoadingSkeleton />;
  return (
    <ScrollArea className="h-full min-w-72 border-r bg-background p-4">
      {executions?.map((execution) => {
        const statusConfig = getStatusConfig(execution.status);
        const duration = execution.finishedAt
          ? differenceInSeconds(new Date(execution.finishedAt), new Date(execution.startedAt))
          : null;

        return (
          <div
            key={execution.id}
            className={`flex cursor-pointer shadow transition-all hover:bg-accent`}
            onClick={() => onSelectExecution?.(execution)}
          >
            <div className={cn("w-1", statusConfig.color)}></div>
            <div className="flex w-full flex-col gap-1 p-4">
              <div className="flex items-center justify-between">
                <p className="text-base font-medium">
                  {formatDate(new Date(execution.finishedAt || execution.startedAt))}
                </p>
                <span className={cn("text-xs font-medium", statusConfig.textClass)}>
                  {statusConfig.label}
                </span>
              </div>

              <span className="text-xs text-muted-foreground">Completed in {duration} seconds</span>

              {(execution.status === WorkflowExecutionStatus.Running ||
                execution.status === WorkflowExecutionStatus.Pending ||
                execution.status === WorkflowExecutionStatus.Queued) && (
                <span className="text-xs text-muted-foreground">
                  Started {formatDate(new Date(execution.startedAt))}
                </span>
              )}
            </div>
          </div>
        );
      })}
    </ScrollArea>
  );
};
