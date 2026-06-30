import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { workflowService } from "../services/workflow.service";
import {
  IGetWorkflowsPayload,
  IGetWorkflowByIdPayload,
  IGetWorkflowExecutionsPayload,
  IGetWorkflowExecutionByIdPayload,
  ICreateWorkflowVersionPayload,
  IGetWorkflowVersionsPayload,
  IPublishWorkflowPayload,
  IUnpublishWorkflowPayload,
  IRestoreWorkflowPayload,
  IGetWorkflowByVersionPayload,
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
    enabled: !!payload.projectKey,
  });
};

export const useGetWorkflowByVersion = (payload: IGetWorkflowByVersionPayload) => {
  return useQuery({
    queryKey: ["workflow-version", payload],
    queryFn: () => workflowService.getWorkflowByVersion(payload),
    enabled: !!payload.projectKey && !!payload.workflowId && !!payload.versionId,
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
