import { serviceInstances } from "@/lib/http-client";
import { SCHEDULER_ENDPOINTS } from "../constants/endpoint.constant";
import {
  IBaseResponse,
  ICreateSchedulePayload,
  IDeleteSchedulePayload,
  IGetSchedulesPayload,
  IGetSchedulesResponse,
  IUpdateSchedulePayload,
} from "../types/schedule.service.type";

export class ScheduleService {
  private readonly logicHttpClient = serviceInstances.logicService;

  getSchedules = (payload: IGetSchedulesPayload): Promise<IGetSchedulesResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.GET_ALL, payload);
  };

  createSchedule = (payload: ICreateSchedulePayload): Promise<IBaseResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.CREATE, payload);
  };

  updateSchedule = (payload: IUpdateSchedulePayload): Promise<IBaseResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.UPDATE, payload);
  };

  deleteSchedule = (payload: IDeleteSchedulePayload): Promise<IBaseResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.DELETE, payload);
  };
}

export const scheduleService = new ScheduleService();
