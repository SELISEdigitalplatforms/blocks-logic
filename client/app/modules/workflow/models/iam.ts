export interface IGetClientsPayload {
  projectKey: string;
}
export interface IClientConfigResponse {
  scope: string;
  itemId: string;
  name: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  clientSecret: string;
  roles: string[];
  isActive: boolean;
  audiences: string[];
}

export interface IOrganization {
  itemId: string;
  name: string;
  description?: string;
  [key: string]: unknown;
}

export interface IRole {
  itemId: string;
  name: string;
  description?: string;
  slug: string;
  [key: string]: unknown;
}

export interface IPermission {
  itemId: string;
  name: string;
  description?: string;
  resource:string;
  [key: string]: unknown;
}

export type MatchMode = "and" | "or";

export interface IListSortPayload {
  property?: string;
  isDescending?: boolean;
}

export interface IListPagePayload {
  page?: number;
  pageSize?: number;
  sort?: IListSortPayload;
}

export interface IGetOrganizationsPayload extends IListPagePayload {}

export interface IGetOrganizationsResponse {
  organizations: IOrganization[];
  isSuccess: boolean;
  totalCount: number;
  errors:unknown
}

export interface IGetRolesResponse {
  data: IRole[];
  totalCount: number;
  errors: unknown;
}

export interface IGetPermissionsResponse {
  data: IPermission[];
  totalCount: number;
  errors: unknown;
}
export interface IGetRolesPayload extends IListPagePayload {
  organizationId?: string;
  search?: string;
}

export interface IGetPermissionsPayload extends IListPagePayload {

}

export interface IPagedResponse<T> {
  items: T[];
  total?: number;
  page?: number;
  pageSize?: number;
  data?: T[];
  totalCount?: number;
}