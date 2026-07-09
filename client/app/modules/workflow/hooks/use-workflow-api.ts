import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useWorkflowStore } from "../store";
import { useWorkflow } from "./use-workflow";
import { workflowService } from "../services/workflow.service";
import {
  IGetWorkflowsPayload,
  IGetWorkflowByIdPayload,
  IGetWorkflowExecutionsPayload,
  IGetWorkflowExecutionByIdPayload,
  IGetWorkflowVersionsPayload,
  IGetWorkflowByVersionPayload,
  IGetLastSuccessfulExecutionPayload,
} from "../types/workflow.service.type";
import { showErrorToast } from "@/hooks/use-toast";

export const useGetWorkflows = (options: IGetWorkflowsPayload) => {
  return useQuery({
    queryKey: ["workflows", options],
    queryFn: () => workflowService.getWorkflows(options),
  });
};

export const useGetWorkflowById = (payload: IGetWorkflowByIdPayload) => {
  return useQuery({
    queryKey: ["workflow", payload],
    queryFn: () => workflowService.getWorkflowById(payload),
    enabled: !!payload.id,
  });
};

export const useCreateWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "create"],
    mutationFn: workflowService.createWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
    },
  });
};

export const useDuplicateWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "duplicate"],
    mutationFn: workflowService.duplicateWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
    },
  });
};

export const useUpdateWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "update"],
    mutationFn: workflowService.updateWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
    },
  });
};

export const useDeleteWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "delete"],
    mutationFn: workflowService.deleteWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
    },
  });
};

export const useGetWorkflowExecutions = (
  payload: IGetWorkflowExecutionsPayload,
) => {
  return useQuery({
    queryKey: ["workflow-executions", payload],
    queryFn: () => workflowService.getWorkflowExecutions(payload),
    enabled: !!payload.workflowId,
    refetchInterval: 5000,
  });
};

export const useGetWorkflowExecutionById = (
  payload: IGetWorkflowExecutionByIdPayload,
) => {
  return useQuery({
    queryKey: ["workflow-execution", payload],
    queryFn: () => workflowService.getWorkflowExecutionById(payload),
    enabled: !!payload.executionId,
    refetchInterval: 5000,
  });
};

export const useCreateWorkflowVersion = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow-version", "create"],
    mutationFn: workflowService.createWorkflowVersion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};

export const useGetWorkflowVersions = (payload: IGetWorkflowVersionsPayload) => {
  return useQuery({
    queryKey: ["workflow-versions", payload],
    queryFn: () => workflowService.getWorkflowVersions(payload),
    enabled: !!payload.workflowId,
  });
};

export const useGetWorkflowByVersion = (payload: IGetWorkflowByVersionPayload) => {
  return useQuery({
    queryKey: ["workflow-version", payload],
    queryFn: () => workflowService.getWorkflowByVersion(payload),
    enabled: !!payload.workflowId && !!payload.versionId,
  });
};

export const usePublishWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "publish"],
    mutationFn: workflowService.publishWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};
export const usePublishNewWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "publish"],
    mutationFn: workflowService.publishWorkflowNewVersion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};

export const useUnpublishWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "unpublish"],
    mutationFn: workflowService.unpublishWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
    },
  });
};

export const useRestoreWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "restore"],
    mutationFn: workflowService.restoreWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};

export const useUpdateWorkflowVersion = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow-version", "update"],
    mutationFn: workflowService.updateWorkflowVersion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
    },
  });
};

export const useStepExecutionHandler = () => {
  const queryClient = useQueryClient();
  const { setStepExecutionData } = useWorkflow();

  const handleExecuteStep = async (executionId: string) => {
    try {
      const data = await queryClient.fetchQuery({
        queryKey: ["workflow-execution", { executionId }],
        queryFn: () => workflowService.getWorkflowExecutionById({ executionId }),
      });
      if (data) {
        setStepExecutionData(data);
      }
    } catch (e: any) {
      showErrorToast({ errors: e.message || "Failed to fetch step execution" });
    }
  };

  return { handleExecuteStep };
};

export const useGetLastSuccessfulExecution = (
  payload: IGetLastSuccessfulExecutionPayload,
) => {
  return useQuery({
    queryKey: ["workflow-last-successful-execution", payload],
    queryFn: () => workflowService.getLastSuccessfulExecution(payload),
    enabled: !!payload.workflowId,
  });
};

export const useStepExecute = () => {
  return useMutation({
    mutationKey: ["workflow", "step-execute"],
    mutationFn: workflowService.stepExecute,
  });
};

export const useExecuteTriggerListener = () => {
  const workflowId = useWorkflowStore((state) => state.workflowId);

  return useMutation({
    mutationKey: ["workflow", "trigger-listener"],
    mutationFn: async ({ triggerId, enableListener, completionNodeId }: { triggerId: string; enableListener: boolean; completionNodeId?: string }) => {
      if (!workflowId) return;
      return workflowService.triggerListener({
        WorkflowId: workflowId,
        TriggerId: triggerId,
        EnableListener: enableListener,
        ...(completionNodeId && { CompletionNodeId: completionNodeId }),
      });
    },
  });
};
