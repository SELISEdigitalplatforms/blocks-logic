import { http } from "@/lib/http-client";
import { API_SETTINGS_ENDPOINTS } from "../constants/endpoint.constant";
import {
  IGetApiEndpointsPayload,
  IGetApiEndpointsResponse,
  IUpdateApiEndpointPayload,
  IUpdateApiEndpointResponse,
  IBulkUpdateApiEndpointsPayload,
  IBulkUpdateApiEndpointsResponse,
  IRemoveApiEndpointsPayload,
  IRemoveApiEndpointsResponse,
} from "../models/api-endpoint.model";

class ApiSettingsService {
  getEndpoints(payload: IGetApiEndpointsPayload): Promise<IGetApiEndpointsResponse> {
    return http.get(`${API_SETTINGS_ENDPOINTS.GETS}?ProjectKey=${payload.projectKey}`);
  }

  updateEndpoint(payload: IUpdateApiEndpointPayload): Promise<IUpdateApiEndpointResponse> {
    return http.post(API_SETTINGS_ENDPOINTS.UPDATE, payload);
  }

  bulkUpdate(payload: IBulkUpdateApiEndpointsPayload): Promise<IBulkUpdateApiEndpointsResponse> {
    return http.post(API_SETTINGS_ENDPOINTS.BULK_UPDATE, payload);
  }

  removeEndpoints(payload: IRemoveApiEndpointsPayload): Promise<IRemoveApiEndpointsResponse> {
    return http.post(API_SETTINGS_ENDPOINTS.REMOVE, payload);
  }
}

export const apiSettingsService = new ApiSettingsService();
