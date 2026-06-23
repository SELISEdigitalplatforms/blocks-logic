/* eslint-disable @typescript-eslint/no-explicit-any */
import { serviceInstances } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { IClientConfigResponse, IGetClientsPayload } from "../models/iam";
import { AUTH_CLIENT_ENDPOINTS } from "../constants/iam.endpoint.constant";

export class AuthClientsService {
  private readonly IamHttpClient = serviceInstances.iamService;
  getClientCredentials(
    payload: IGetClientsPayload,
  ): Promise<IClientConfigResponse[]> {
    const baseUrl = getRuntimeEnv("BLOCKS_IAM_BASE_URL") || getRuntimeEnv("BLOCKS_LOGIC_BASE_URL");
    return this.IamHttpClient.get(
      `${baseUrl}${AUTH_CLIENT_ENDPOINTS.GET_CLIENT_CREDENTIALS}?ProjectKey=${payload.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }
}

export const authClientService = {
  clients: new AuthClientsService(),
};
