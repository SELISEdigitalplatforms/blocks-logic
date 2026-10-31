import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { eventTriggersService } from "../services/event-triggers.service";
import { IBlocksEventCatalogResponse, IUpdateBlocksEventFlagPayload } from "../types/event-triggers.type";

export const EVENT_TRIGGERS_QUERY_KEY = ["event-triggers"] as const;

export const useGetEventTriggers = () => {
  return useQuery({
    queryKey: [...EVENT_TRIGGERS_QUERY_KEY, "all"],
    queryFn: eventTriggersService.getAll,
  });
};

/**
 * Whether the caller may change the catalog. The server's own permission check answers: any failure (403
 * for a read-only user) means read-only, so the switches are never shown enabled to someone who cannot save.
 */
export const useCanManageEventTriggers = () => {
  const query = useQuery({
    queryKey: [...EVENT_TRIGGERS_QUERY_KEY, "can-manage"],
    queryFn: eventTriggersService.canManage,
    retry: false,
    staleTime: Infinity,
  });
  return { canManage: query.data?.canManage === true, isLoading: query.isLoading };
};

/**
 * Saves one flag. The cached list is updated in place on success; the caller shows the switch's pending and
 * reverted state itself, so only that row changes.
 */
export const useUpdateEventTrigger = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...EVENT_TRIGGERS_QUERY_KEY, "update"],
    mutationFn: (payload: IUpdateBlocksEventFlagPayload) => eventTriggersService.update(payload),
    onSuccess: (result, payload) => {
      if (!result.isSuccess) return;
      queryClient.setQueryData<IBlocksEventCatalogResponse>([...EVENT_TRIGGERS_QUERY_KEY, "all"], (current) =>
        current
          ? {
              ...current,
              items: current.items.map((item) =>
                item.eventName === payload.eventName
                  ? {
                      ...item,
                      ...(payload.isValidForWorkflow !== undefined && { isValidForWorkflow: payload.isValidForWorkflow }),
                      ...(payload.isValidForFunction !== undefined && { isValidForFunction: payload.isValidForFunction }),
                    }
                  : item,
              ),
            }
          : current,
      );
    },
  });
};
