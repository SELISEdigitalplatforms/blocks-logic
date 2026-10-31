import { serviceInstances } from "@/lib/http-client";
import { EVENT_TRIGGERS_ENDPOINTS } from "../constants/endpoint.constant";
import {
  IBlocksEventCatalogResponse,
  IBlocksEventUsageResponse,
  IBlocksEventWorkflowEventsResponse,
  IUpdateBlocksEventFlagPayload,
  IUpdateBlocksEventFlagResponse,
} from "../types/event-triggers.type";

export class EventTriggersService {
  private readonly logicHttpClient = serviceInstances.logicService;

  getAll = (): Promise<IBlocksEventCatalogResponse> => {
    return this.logicHttpClient.get(EVENT_TRIGGERS_ENDPOINTS.GET_ALL);
  };

  getWorkflowEvents = (): Promise<IBlocksEventWorkflowEventsResponse> => {
    return this.logicHttpClient.get(EVENT_TRIGGERS_ENDPOINTS.GET_WORKFLOW_EVENTS);
  };

  getUsage = (eventName: string): Promise<IBlocksEventUsageResponse> => {
    const params = new URLSearchParams({ eventName });
    return this.logicHttpClient.get(`${EVENT_TRIGGERS_ENDPOINTS.GET_USAGE}?${params.toString()}`);
  };

  /** Resolves for callers with workflow manage permission; the server answers 403 to anyone else. */
  canManage = (): Promise<{ canManage: boolean }> => {
    return this.logicHttpClient.get(EVENT_TRIGGERS_ENDPOINTS.CAN_MANAGE);
  };

  update = (payload: IUpdateBlocksEventFlagPayload): Promise<IUpdateBlocksEventFlagResponse> => {
    return this.logicHttpClient.post(EVENT_TRIGGERS_ENDPOINTS.UPDATE, payload);
  };
}

export const eventTriggersService = new EventTriggersService();
