/* eslint-disable @typescript-eslint/no-explicit-any */
import { http } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { IClientConfigResponse, IGetClientsPayload } from "../models/iam";
import { AUTH_CLIENT_ENDPOINTS } from "../constants/iam.endpoint.constant";

export class AuthClientsService {
  getClientCredentials(
    payload: IGetClientsPayload,
  ): Promise<IClientConfigResponse[]> {
    const baseUrl = getRuntimeEnv("BLOCKS_IDP_BASE_URL") || getRuntimeEnv("BLOCKS_API_BASE_URL");
    return http.get(
      `${baseUrl}${AUTH_CLIENT_ENDPOINTS.GET_CLIENT_CREDENTIALS}?ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }
}

export const authClientService = {
  clients: new AuthClientsService(),
};
