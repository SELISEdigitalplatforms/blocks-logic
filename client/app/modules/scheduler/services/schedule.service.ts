import { serviceInstances } from "@/lib/http-client";
import { SCHEDULER_ENDPOINTS } from "../constants/endpoint.constant";
import {
  IBaseMutationResponse,
  IBaseResponse,
  ICreateSchedulePayload,
  IDeleteSchedulePayload,
  IGetScheduleExecutionPayload,
  IGetScheduleExecutionResponse,
  IGetScheduleExecutionsPayload,
  IGetScheduleExecutionsResponse,
  IGetSchedulesPayload,
  IGetSchedulesResponse,
  IPreviewScheduleRunsPayload,
  IPreviewScheduleRunsResponse,
  IUpdateSchedulePayload,
} from "../types/schedule.service.type";

export class ScheduleService {
  private readonly logicHttpClient = serviceInstances.logicService;

  getSchedules = (payload: IGetSchedulesPayload): Promise<IGetSchedulesResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.GET_ALL, payload);
  };

  createSchedule = (payload: ICreateSchedulePayload): Promise<IBaseMutationResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.CREATE, payload);
  };

  updateSchedule = (payload: IUpdateSchedulePayload): Promise<IBaseMutationResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.UPDATE, payload);
  };

  deleteSchedule = (payload: IDeleteSchedulePayload): Promise<IBaseResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.DELETE, payload);
  };

  getScheduleExecutions = (
    payload: IGetScheduleExecutionsPayload,
  ): Promise<IGetScheduleExecutionsResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.GET_EXECUTIONS, payload);
  };

  getScheduleExecution = (
    payload: IGetScheduleExecutionPayload,
  ): Promise<IGetScheduleExecutionResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.GET_EXECUTION, payload);
  };

  previewScheduleRuns = (payload: IPreviewScheduleRunsPayload): Promise<IPreviewScheduleRunsResponse> => {
    return this.logicHttpClient.post(SCHEDULER_ENDPOINTS.PREVIEW_RUNS, payload);
  };
}

export const scheduleService = new ScheduleService();
