import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import type {
  IEventSettings,
  ISaveEventSettingsPayload,
} from "../types/event.types";
import { mergeSettings } from "../utils/events";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

/** How often the queue card reloads while it is on screen (a hidden tab does not poll). */
export const QUEUE_STATUS_REFETCH_MS = 10_000;

const eventSettingsKey = (functionId: string) => [FUNCTIONS_QUERY_KEY, "events", "settings", functionId];

export const useGetEventCatalog = () => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "events", "catalog"],
    queryFn: () => functionService.getEventCatalog(),
    staleTime: Infinity,
  });
};

/** The draft's events and trigger events. One query shared by the Events tab and the trigger card. */
export const useGetEventSettings = (functionId: string | undefined) => {
  return useQuery({
    queryKey: eventSettingsKey(functionId ?? ""),
    queryFn: () => functionService.getEventSettings(functionId!),
    enabled: !!functionId,
  });
};

export type EventSettingsPatch = Partial<Pick<IEventSettings, "events" | "triggerEvents">>;

/**
 * Saves a part of the event settings. The body is always the latest full settings with the patch
 * applied, so the Events tab and the trigger card never overwrite each other. Saves of one function
 * run one after another (`scope`), each reading the settings the one before it wrote.
 */
export const useSaveEventSettings = (functionId: string) => {
  const queryClient = useQueryClient();
  const key = eventSettingsKey(functionId);
  return useMutation({
    mutationKey: [FUNCTIONS_QUERY_KEY, "events", "save", functionId],
    scope: { id: `function-events-${functionId}` },
    mutationFn: async (patch: EventSettingsPatch): Promise<ISaveEventSettingsPayload> => {
      const latest =
        queryClient.getQueryData<IEventSettings>(key) ??
        (await queryClient.fetchQuery<IEventSettings>({
          queryKey: key,
          queryFn: () => functionService.getEventSettings(functionId),
        }));
      const body = mergeSettings(latest, functionId, patch);
      await functionService.saveEventSettings(body);
      return body;
    },
    onSuccess: (body) => {
      queryClient.setQueryData<IEventSettings>(key, body);
      queryClient.invalidateQueries({ queryKey: key });
    },
  });
};

export const useGetRunEvents = (runId: string | undefined) => {
  return useQuery({
    queryKey: [FUNCTIONS_QUERY_KEY, "runs", "events", runId],
    queryFn: () => functionService.getRunEvents(runId!),
    enabled: !!runId,
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
