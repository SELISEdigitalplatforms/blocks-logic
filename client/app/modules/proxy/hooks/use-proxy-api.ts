import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PROXY_QUERY_KEY } from "../constants";
import { proxyService } from "../services";
import { ProxyFormValues, ProxyListParams, ProxyLogFilter, ProxyTestRequest } from "../types";

export const useGetProxies = (params: ProxyListParams = {}) =>
  useQuery({
    queryKey: [...PROXY_QUERY_KEY, params],
    queryFn: () => proxyService.getAll(params),
  });

export const useGetProxyById = (id?: string) =>
  useQuery({
    queryKey: [...PROXY_QUERY_KEY, "detail", id],
    queryFn: () => (id ? proxyService.get(id) : null),
    enabled: !!id,
  });

export const useCreateProxy = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...PROXY_QUERY_KEY, "create"],
    mutationFn: (values: ProxyFormValues) => proxyService.create(values),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROXY_QUERY_KEY }),
  });
};

export const useUpdateProxy = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...PROXY_QUERY_KEY, "update"],
    mutationFn: ({ id, values }: { id: string; values: ProxyFormValues }) =>
      proxyService.update({ id, values }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROXY_QUERY_KEY }),
  });
};

export const useToggleProxy = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...PROXY_QUERY_KEY, "toggle"],
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
      proxyService.toggle({ id, enabled }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROXY_QUERY_KEY }),
  });
};

export const useDeleteProxy = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...PROXY_QUERY_KEY, "delete"],
    mutationFn: (id: string) => proxyService.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROXY_QUERY_KEY }),
  });
};

export const useGetProxyExecutions = (
  proxyId?: string,
  filter: ProxyLogFilter = "all",
  options: { live?: boolean; enabled?: boolean; page?: number; pageSize?: number } = {},
) =>
  useQuery({
    queryKey: [
      ...PROXY_QUERY_KEY,
      "executions",
      proxyId,
      filter,
      options.page ?? 0,
      options.pageSize ?? null,
      options.live,
    ],
    queryFn: () =>
      proxyService.getExecutions(proxyId!, filter, {
        live: options.live,
        page: options.page,
        pageSize: options.pageSize,
      }),
    enabled: Boolean(proxyId) && (options.enabled ?? true),
    refetchInterval: options.live ? 3000 : false,
    // Keep the current page visible while the next one loads instead of flashing the skeleton.
    placeholderData: keepPreviousData,
  });

export const useGetProxyExecution = (
  proxyId: string | undefined,
  executionId: string | null,
  options: { enabled?: boolean } = {},
) =>
  useQuery({
    queryKey: [...PROXY_QUERY_KEY, "execution", proxyId, executionId],
    queryFn: () => proxyService.getExecution(proxyId!, executionId!),
    enabled: Boolean(proxyId) && Boolean(executionId) && (options.enabled ?? true),
    staleTime: Infinity,
  });

export const useGetProxyOverview = (proxyId?: string, options: { enabled?: boolean } = {}) =>
  useQuery({
    queryKey: [...PROXY_QUERY_KEY, "overview", proxyId],
    queryFn: () => proxyService.getOverview(proxyId!),
    enabled: Boolean(proxyId) && (options.enabled ?? true),
  });

export const useGetProxyVersions = (proxyId?: string) =>
  useQuery({
    queryKey: [...PROXY_QUERY_KEY, "versions", proxyId],
    queryFn: () => proxyService.getVersions(proxyId!),
    enabled: Boolean(proxyId),
  });

export const useRevertProxyVersion = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...PROXY_QUERY_KEY, "revert"],
    mutationFn: ({ proxyId, versionId }: { proxyId: string; versionId: string }) =>
      proxyService.revert({ proxyId, versionId }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROXY_QUERY_KEY }),
  });
};

export const useSendProxyTestRequest = () =>
  useMutation({
    mutationKey: [...PROXY_QUERY_KEY, "test"],
    mutationFn: (request: ProxyTestRequest) => proxyService.test(request),
  });

export const useExportProxyExecutionCsv = () =>
  useMutation({
    mutationKey: [...PROXY_QUERY_KEY, "export-executions"],
    mutationFn: ({ proxyId, filter }: { proxyId: string; filter: ProxyLogFilter }) =>
      proxyService.exportExecutionsCsv({ proxyId, filter }),
  });
