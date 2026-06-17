export interface IDataServiceConfigurationResponse {
  errors: unknown | null;
  isSuccess: boolean;
  data:
    | unknown
    | null
    | {
        itemId: string;
        projectKey: string;
        projectShortKey: string;
      };
}

export interface IGetSchemaListPayload {
  keyword?: string;
  pageNo: number;
  pageSize: number;
  sortDescending?: boolean;
  sortBy?: "CreatedDate";
  schemaName?: string;
  projectKey: string;
  schemaType?: string | number;
}

export interface IDataAccessRuleSet {
  roles: string[];
  permissions: string[];
  users: string[];
}

export interface IDataAccessRuleSetDto {
  roles: string[] | null;
  permissions: string[] | null;
  users: string[] | null;
}

export interface ISchemaFieldValidation {
  type: number;
  value: string;
  secondaryValue: string;
  errorMessage: string;
  isActive: boolean;
}

export interface ISchemaFieldValidationData {
  itemId: string;
  schemaId: string;
  fieldName: string;
  validations: ISchemaFieldValidation[];
  createdDate: string;
  lastUpdatedDate: string;
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

export interface IField {
  name: string;
  type: string;
  isArray: boolean;
  isPIIData?: boolean;
  isUniqueData?: boolean;
  description?: string;
  readAccess?: IDataAccessRuleSet;
  writeAccess?: IDataAccessRuleSet;
  deleteAccess?: IDataAccessRuleSet;
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
  fields?: IField[];
}

export interface ISchemaDetails {
  id: string;
  schemaName: string;
  schemaType: number;
  collectionName: string;
  fields: IField[];
  totalPermissions: number;
  totalRoles: number;
  totalUsers: number;
  readAccess?: IDataAccessRuleSet;
  writeAccess?: IDataAccessRuleSet;
  deleteAccess?: IDataAccessRuleSet;
  projectKey?: string;
  isRlsEnabled?: boolean;
  isClsEnabled?: boolean;
  projectShortKey: string;
  totalSchemaReferences: number;
  schemaReferences: string[];
  rowAccessLevel?: number;
  columnAccessLevel?: number;
  deleteAccessLevel: number;
  editAccessLevel: number;
  readAccessLevel: number;
  writeAccessLevel: number;
}

export interface IPermissionAggregation {
  totalCustomPermission: number;
  totalPublicPermission: number;
  totalUserPermission: number;
}

// Permission counts
export interface IGetSchemaListResponse extends IDataServiceConfigurationResponse {
  data: {
    items: ISchemaDetails[];
    totalCount: number;
    schemas: {
      items: ISchemaDetails[];
      totalCount: number;
    };
    aggregation: IPermissionAggregation;
  };
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

export interface IGetSchemaDetailsResponse extends IDataServiceConfigurationResponse {
  data: {
    id: string;
    schemaName: string;
    schemaType: number;
    collectionName: string;
    fields: IRemoteSchemaField[];
    totalCount: number;
    totalPermissions: number;
    totalRoles: number;
    totalUsers: number;
    readAccess?: IDataAccessRuleSetDto | null;
    writeAccess?: IDataAccessRuleSetDto | null;
    deleteAccess?: IDataAccessRuleSetDto | null;
    projectKey?: string;
    isRlsEnabled?: boolean;
    isClsEnabled?: boolean;
    projectShortKey: string;
    totalSchemaReferences: number;
    schemaReferences: string[];
    rowAccessLevel?: number;
    columnAccessLevel?: number;
    deleteAccessLevel: number;
    editAccessLevel: number;
    readAccessLevel: number;
    writeAccessLevel: number;
  };
}
