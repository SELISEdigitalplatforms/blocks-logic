import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import type {
  IEventSettings,
  ISaveEventSettingsPayload,
} from "../types/event.types";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

/** How often the queue card reloads while it is on screen (a hidden tab does not poll). */
export const QUEUE_STATUS_REFETCH_MS = 10_000;

const eventSettingsKey = (functionId: string) => [FUNCTIONS_QUERY_KEY, "events", "settings", functionId];

/** The draft's trigger events. */
export const useGetEventSettings = (functionId: string | undefined) => {
  return useQuery({
    queryKey: eventSettingsKey(functionId ?? ""),
    queryFn: () => functionService.getEventSettings(functionId!),
    enabled: !!functionId,
  });
};

export type EventSettingsPatch = Partial<Pick<IEventSettings, "triggerEvents" | "actionsEnabled">>;

/**
 * Saves the draft's trigger events and the actions switch. A patch may hold one of the two: the
 * other is read from the cache when the save runs, not when it was queued. Saves of one function
 * run one after another (`scope`), so each one sees the result of the one before it.
 */
export const useSaveEventSettings = (functionId: string) => {
  const queryClient = useQueryClient();
  const key = eventSettingsKey(functionId);
  return useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "events", "save", functionId],
    scope: { id: `function-events-${functionId}` },
    mutationFn: async (patch: EventSettingsPatch): Promise<ISaveEventSettingsPayload> => {
      const current = queryClient.getQueryData<IEventSettings>(key);
      const body: ISaveEventSettingsPayload = {
        functionId,
        triggerEvents: patch.triggerEvents ?? current?.triggerEvents ?? [],
        actionsEnabled: patch.actionsEnabled ?? current?.actionsEnabled ?? true,
      };
      await functionService.saveEventSettings(body);
      return body;
    },
    onSuccess: (body) => {
      queryClient.setQueryData<IEventSettings>(key, body);
      queryClient.invalidateQueries({ queryKey: key });
    },
  });
};

export const useGetQueueStatus = (functionId: string | undefined) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "queue", functionId],
    queryFn: () => functionService.getQueueStatus(functionId!),
    enabled: !!functionId,
    refetchInterval: QUEUE_STATUS_REFETCH_MS,
  });
};
