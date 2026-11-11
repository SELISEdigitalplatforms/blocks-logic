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
  IGetRolesPayload,
  IGetRolesResponse,
  IOrganizationConfig,
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

  /** The tenant's organization settings (`isMultiOrgEnabled`); the tenant comes from the token. */
  getOrganizationConfig(): Promise<IOrganizationConfig> {
    return this.IamHttpClient.get(
      `${this.baseUrl}${IAM_AUTHORIZATION_ENDPOINTS.GET_ORGANIZATIONS}/config/admin`,
      undefined,
      { absoluteUrl: true },
    );
  }

  /**
   * Roles, optionally of one organization. IAM honours `organizationId` only for callers in the
   * default organization; it is sent only when it is a non-empty string, so the body is otherwise
   * unchanged.
   */
  getRoles(payload: IGetRolesPayload = {}): Promise<IGetRolesResponse> {
    const body: Record<string, unknown> = {
      page: payload.page ?? 0,
      pageSize: payload.pageSize ?? 100,
      filter: { search: payload.search ?? "" },
      sort: payload.sort ?? { property: "Name", isDescending: false },
    };
    if (typeof payload.organizationId === "string" && payload.organizationId !== "") {
      body.organizationId = payload.organizationId;
    }
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
        search: payload.search ?? "",
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
