import { ISchemaFieldValidationData } from "../models/data-service";

export interface ResolvedSchemaField {
  name: string;
  type: string;
  isArray: boolean;
  description?: string;
  fields?: ResolvedSchemaField[];
}

export interface IDataAccessRuleSetDto {
  roles: string[] | null;
  permissions: string[] | null;
  users: string[] | null;
}

export interface IFieldValidationRule extends ISchemaFieldValidationData {
  deletedDate: string | null;
  isDeleted: boolean;
  createdBy: string;
  language: string | null;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
}

export interface IRemoteSchemaField {
  name: string;
  type: string;
  isArray: boolean;
  isPIIData?: boolean;
  isUniqueData?: boolean;
  description?: string;
  readAccess?: IDataAccessRuleSetDto | null;
  writeAccess?: IDataAccessRuleSetDto | null;
  deleteAccess?: IDataAccessRuleSetDto | null;
  totalRoles?: number;
  totalUsers?: number;
  totalPermissions?: number;
  deleteAccessLevel?: number;
  editAccessLevel?: number;
  readAccessLevel?: number;
  writeAccessLevel?: number;
  totalValidationRules?: number;
  validationRule?: IFieldValidationRule | null;
  /** Nested fields for custom/child type properties */
  fields?: IRemoteSchemaField[];
}
