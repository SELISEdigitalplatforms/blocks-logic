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
