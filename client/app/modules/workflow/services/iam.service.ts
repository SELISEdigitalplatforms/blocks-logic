/* eslint-disable @typescript-eslint/no-explicit-any */
import { serviceInstances } from "@/lib/http-client";
import { getRuntimeEnv } from "@seliseblocks/genesis-os";
import { getLogicBaseUrl } from "@/lib/logic-base-url";
import {
  IClientConfigResponse,
  IGetOrganizationsPayload,
  IGetOrganizationsResponse,
  IGetPermissionsPayload,
  IGetPermissionsResponse,
  IGetRolesResponse,
} from "../models/iam";
import { AUTH_CLIENT_ENDPOINTS, IAM_AUTHORIZATION_ENDPOINTS } from "../constants/iam.endpoint.constant";

export class AuthClientsService {
  private readonly IamHttpClient = serviceInstances.iamService;
  getClientCredentials(): Promise<IClientConfigResponse[]> {
    const baseUrl = getRuntimeEnv("BLOCKS_IAM_BASE_URL") || getLogicBaseUrl();
    return this.IamHttpClient.get(
      `${baseUrl}${AUTH_CLIENT_ENDPOINTS.GET_CLIENT_CREDENTIALS}`,
      undefined,
      { absoluteUrl: true },
    );
  }
}


export class IamService {
  private readonly IamHttpClient = serviceInstances.iamService;
  private readonly baseUrl =
    getRuntimeEnv("BLOCKS_IAM_BASE_URL") || getLogicBaseUrl();

  getOrganizations(payload: IGetOrganizationsPayload = {}): Promise<IGetOrganizationsResponse> {
    const page = payload.page ?? 0;
    const pageSize = payload.pageSize ?? 50;
    return this.IamHttpClient
      .get(
        `${this.baseUrl}${IAM_AUTHORIZATION_ENDPOINTS.GET_ORGANIZATIONS}?Page=${page}&PageSize=${pageSize}`,
        undefined,
        { absoluteUrl: true },
      );
  }

  /**
   * Lists roles. Roles are org-scoped in IAM, so `organizationId` narrows the list to that
   * organization; omitted or blank, IAM answers for the caller's own organization.
   */
  getRoles(payload: { organizationId?: string } = {}): Promise<IGetRolesResponse> {
    const organizationId = payload.organizationId?.trim();
    const body = {
      page: 0,
      pageSize: 100,
      sort: { property: "Name", isDescending: false },
      ...(organizationId ? { organizationId } : {}),
      filter: { search: "" },
    };
    return this.IamHttpClient
      .post(
        `${this.baseUrl}${IAM_AUTHORIZATION_ENDPOINTS.GET_ROLES}`,
        body as any,
        undefined,
        { absoluteUrl: true },
      );
  }

  getPermissions(payload: IGetPermissionsPayload): Promise<IGetPermissionsResponse> {
    const body = {
      page: payload.page ?? 0,
      pageSize: payload.pageSize ?? 500,
      sort: payload.sort ?? { property: "Name", isDescending: false },
      filter: {
        search: "",
        isBuiltIn: "",
        resourceGroup: "",
      },
    };
    return this.IamHttpClient
      .post(
        `${this.baseUrl}${IAM_AUTHORIZATION_ENDPOINTS.GET_PERMISSIONS}`,
        body as any,
        undefined,
        { absoluteUrl: true },
      );
  }
}

export const authClientService = {
  clients: new AuthClientsService(),
};

export const iamService = new IamService();
