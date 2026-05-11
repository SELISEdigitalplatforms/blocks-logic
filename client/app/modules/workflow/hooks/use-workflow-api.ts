import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { workflowService } from "../services/workflow.service";
import {
  IGetWorkflowsPayload,
  IGetWorkflowByIdPayload,
  IGetWorkflowExecutionsPayload,
  IGetWorkflowExecutionByIdPayload,
} from "../types/workflow.service.type";

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
    enabled: !!payload.id || !!payload.projectKey,
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
    enabled: !!payload.projectKey && !!payload.workflowId,
    refetchInterval: 5000,
  });
};

export const useGetWorkflowExecutionById = (
  payload: IGetWorkflowExecutionByIdPayload,
) => {
  return useQuery({
    queryKey: ["workflow-execution", payload],
    queryFn: () => workflowService.getWorkflowExecutionById(payload),
    enabled: !!payload.projectKey && !!payload.executionId,
    refetchInterval: 5000,
  });
};
