export interface IApiEndpoint {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string | null;
  lastUpdatedBy: string;
  language: string | null;
  organizationIds: string[];
  tags: string[];
  service: string;
  method: string;
  endpoint: string;
  description: string;
  isCaptchaRequired: boolean;
  captchaProvider: string;
  isMfaRequired: boolean;
  mfaType: string;
}

export interface IGetApiEndpointsPayload {
  projectKey: string;
}

export interface IGetApiEndpointsResponse {
  endpoints: IApiEndpoint[];
}

export interface IUpdateApiEndpointPayload {
  projectKey: string;
  itemId: string;
  isCaptchaRequired?: boolean;
  captchaProvider?: string;
  isMfaRequired?: boolean;
  mfaType?: string;
}

export interface IUpdateApiEndpointResponse {
  isSuccess: boolean;
  errors?: string[];
}

export interface IBulkUpdateApiEndpointsPayload {
  projectKey: string;
  itemIds: string[];
  isCaptchaRequired?: boolean;
  isMfaRequired?: boolean;
}

export interface IBulkUpdateApiEndpointsResponse {
  isSuccess: boolean;
  errors?: string[];
}

export interface IRemoveApiEndpointsPayload {
  projectKey: string;
  itemIds: string[];
}

export interface IRemoveApiEndpointsResponse {
  isSuccess: boolean;
  errors?: string[];
}

export interface IServiceGroup {
  service: string;
  description: string;
  icon: string;
  endpoints: IApiEndpoint[];
}
