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