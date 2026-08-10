/* eslint-disable @typescript-eslint/no-explicit-any */
import { serviceInstances } from "@/lib/http-client";
import { getRuntimeEnv } from "@seliseblocks/genesis-os";
import { IClientConfigResponse, IGetClientsPayload, IGetOrganizationsResponse } from "../models/iam";
import { AUTH_CLIENT_ENDPOINTS, IAM_AUTHORIZATION_ENDPOINTS } from "../constants/iam.endpoint.constant";
import {
  IGetOrganizationsPayload,
  IGetPermissionsPayload,
  IGetRolesPayload,
  IOrganization,
  IPermission,
  IRole,
} from "../models/iam";

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

interface ListResponse<T> {
  items?: T[];
  data?: T[];
  results?: T[];
}

const extractList = <T>(response: unknown): T[] => {
  if (Array.isArray(response)) return response as T[];
  const r = response as ListResponse<T>;
  if (Array.isArray(r.items)) return r.items;
  if (Array.isArray(r.data)) return r.data;
  if (Array.isArray(r.results)) return r.results;
  return [];
};

export class IamService {
  private readonly IamHttpClient = serviceInstances.iamService;
  private readonly baseUrl =
    getRuntimeEnv("BLOCKS_IAM_BASE_URL") || getRuntimeEnv("BLOCKS_LOGIC_BASE_URL");

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

  getRoles(): Promise<IRole[]> {
    const body = {
      page: 0,
      pageSize: 100,
      filter: { search: "" },
      sort: { property: "Name", isDescending: false }
    };
    return this.IamHttpClient
      .post(
        `${this.baseUrl}${IAM_AUTHORIZATION_ENDPOINTS.GET_ROLES}`,
        body as any,
        undefined,
        { absoluteUrl: true },
      );
  }

  getPermissions(payload: IGetPermissionsPayload): Promise<IPermission[]> {
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
