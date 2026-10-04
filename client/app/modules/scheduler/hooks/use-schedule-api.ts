import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { scheduleService } from "../services/schedule.service";
import { IGetSchedulesPayload } from "../types/schedule.service.type";

export const useGetSchedules = (payload: IGetSchedulesPayload) => {
  return useQuery({
    queryKey: ["schedules", payload],
    queryFn: () => scheduleService.getSchedules(payload),
  });
};

export const useCreateSchedule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["schedules", "create"],
    mutationFn: scheduleService.createSchedule,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["schedules"] });
    },
  });
};

export const useUpdateSchedule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["schedules", "update"],
    mutationFn: scheduleService.updateSchedule,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["schedules"] });
    },
  });
};

export const useDeleteSchedule = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["schedules", "delete"],
    mutationFn: scheduleService.deleteSchedule,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["schedules"] });
    },
  });
};

export interface IGetScheduleByIdOptions {
  scheduleId?: string;
  enabled?: boolean;
}

/**
 * Hook to retrieve a single schedule by ID.
 *
 * Current implementation:
 * Fetches the schedules list via `scheduleService.getSchedules` (cached by TanStack Query)
 * and finds the matching item with `itemId === scheduleId`.
 *
 * Future replacement:
 * When the backend `GetScheduleById` endpoint is available, swap the `queryFn` with:
 * queryFn: () => scheduleService.getScheduleById({ itemId: scheduleId! })
 */
export const useGetScheduleById = ({ scheduleId, enabled = true }: IGetScheduleByIdOptions) => {
  return useQuery({
    queryKey: ["schedules", "detail", scheduleId],
    queryFn: async () => {
      if (!scheduleId) return null;
      // =========================================================================
      // CURRENT: Fetches via GetSchedules list and filters by scheduleId
      // =========================================================================
      const response = await scheduleService.getSchedules({
        searchKey: "",
        pageNumber: 0,
        pageSize: 100,
      });
      const schedules = response?.data || [];
      const item = schedules.find((s) => s.itemId === scheduleId) ?? null;
      return item;

      // =========================================================================
      // FUTURE: Replace with direct endpoint when ready:
      // const response = await scheduleService.getScheduleById({ itemId: scheduleId });
      // return response?.data ?? null;
      // =========================================================================
    },
    enabled: enabled && !!scheduleId,
  });
};

