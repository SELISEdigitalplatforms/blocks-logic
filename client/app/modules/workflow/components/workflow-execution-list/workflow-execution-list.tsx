"use client";

import { WorkflowExecution } from "@blocks-workflow/types/workflow.service.type";
import { ScrollArea } from "@/components/ui-kits/scroll-area/scroll-area";
import { formatDate, cn } from "@/lib/utils";
import { differenceInSeconds } from "date-fns";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { FlaskConical, Rocket } from "lucide-react";
import { WorkflowExecutionMode } from "../../models/workflow.model";
import {
  getStatusConfig,
  WorkflowExecutionStatus,
} from "../../utils/workflow-execution-list.util";

interface WorkflowExecutionListProps {
  executions: WorkflowExecution[];
  isLoading?: boolean;
  selectedExecutionId?: string;
  onSelectExecution?: (execution: WorkflowExecution) => void;
}

const LoadingSkeleton = () => {
  return (
    <ScrollArea className="h-full w-64 min-w-64 border-r bg-background p-3">
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
  selectedExecutionId,
  onSelectExecution,
}: WorkflowExecutionListProps) => {
  if (isLoading) return <LoadingSkeleton />;
  return (
    <ScrollArea className="h-full w-64 min-w-64 border-r bg-background p-3">
      {executions?.map((execution) => {
        const statusConfig = getStatusConfig(execution.status);
        const StatusIcon = statusConfig.icon;
        const duration = execution.finishedAt
          ? differenceInSeconds(
              new Date(execution.finishedAt),
              new Date(execution.startedAt),
            )
          : null;

        return (
          <div
            key={execution.id}
            className={cn(
              "flex cursor-pointer shadow transition-all hover:bg-accent",
              selectedExecutionId === execution.id && "bg-accent",
            )}
            onClick={() => onSelectExecution?.(execution)}
          >
            <div className={cn("w-1", statusConfig.color)}></div>
            <div className="flex w-full flex-col gap-1 p-4 pr-2">
              <div className="flex flex-col w-full">
                <div className="flex justify-between items-center">
                  <div className="flex items-center gap-2">
                    {execution.executionMode === WorkflowExecutionMode.Production ? (
                      <Rocket className="h-4 w-4 text-blue-500" />
                    ) : (
                      <FlaskConical className="h-4 w-4 text-orange-500" />
                    )}
                    <p className="text-base font-medium">
                      {formatDate(
                        new Date(execution.finishedAt || execution.startedAt),
                      )}
                    </p>
                  </div>
                  <div className="flex items-center gap-1">
                    <StatusIcon className={cn(statusConfig.iconClass, statusConfig.textClass)} />
                  </div>
                </div>
                <div className="pl-6">
                  <span className="text-xs text-muted-foreground">
                    Completed in {duration} seconds
                  </span>
                </div>
              </div>

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
