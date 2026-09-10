import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import {
  ICreateFunctionPayload,
  IDeployFunctionPayload,
  IGetFunctionsPayload,
  IRollbackFunctionPayload,
} from "../types/function.types";

export const FUNCTIONS_QUERY_KEY = "functions";

export const useGetFunctions = (payload: IGetFunctionsPayload) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, payload],
    queryFn: () => functionService.getFunctions(payload),
  });
};

export const useGetLimitsOptions = () => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "limits"],
    queryFn: () => functionService.getLimitsOptions(),
    staleTime: Infinity,
  });
};

export const useCreateFunction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "create"],
    mutationFn: (payload: ICreateFunctionPayload) => functionService.createFunction(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY] });
    },
  });
};

export const useDeleteFunction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "delete"],
    mutationFn: (functionId: string) => functionService.deleteFunction(functionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY] });
    },
  });
};

export const useDeployFunction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "deploy"],
    mutationFn: (payload: IDeployFunctionPayload) => functionService.deployFunction(payload),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY] });
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY, "versions", variables.functionId] });
    },
  });
};

export const useRollbackFunction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "rollback"],
    mutationFn: (payload: IRollbackFunctionPayload) => functionService.rollbackFunction(payload),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY] });
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY, "versions", variables.functionId] });
    },
  });
};
