export interface IDataServiceConfiguration {
  projectKey: string;
  databaseName: string;
  connectionString: string;
  itemId?: string;
}

export interface IDefaultResponse {
  isSuccess: boolean;
  message: string;
  httpStatusCode: number;
  errors: string[];
}

export interface IDataSourceResponse {
  projectKey: string;
  databaseName: string;
  dbConnectionString: string;
  itemId?: string;
  isActive: boolean;
}

export interface IUnadaptedChangeLogsResponse {
  errors: unknown | null;
  isSuccess: boolean;
  data: Array<{
    changeType: number;
  }>;
}

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

export interface ICreateSchemaResponse {
  errors: unknown | null;
  isSuccess: boolean;
  data: {
    acknowledged: boolean;
    itemId: string;
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

export interface IDataSourceFormValues {
  dbConnectionString: string;
  databaseName: string;
}

export interface ICreateSchemaPayload {
  schemaName: string;
  collectionName: string | undefined;
  schemaType: number;
  itemId?: string;
  projectKey: string;
}

export interface ICreateSchemaDefaultValues {
  schemaName: string;
  schemaType: "Entity" | "DTO";
  entityName?: string;
}

export interface IUpdateSchemaStructure {
  schemaDefinitionItemId: string;
  deletableFieldNames?: string[];
  fields: IField[];
  projectKey: string;
}

interface IFieldLevelAccess {
  name: string;
  readAccess: IDataAccessRuleSet;
  writeAccess: IDataAccessRuleSet;
  deleteAccess: IDataAccessRuleSet;
}

export interface ISetDataAccessPayload {
  projectKey: string;
  writeAccess: IDataAccessRuleSet;
  deleteAccess: IDataAccessRuleSet;
  readAccess: IDataAccessRuleSet;
  fields: IFieldLevelAccess[];
  schemaId: string;
}

export interface ISetRowColumnPermissionPayload {
  projectKey: string;
  schemaId: string;
  operation: number;
  policyType: number;
  fieldNames: Array<string>;
  accessLevel: number;
}

export interface ISetDataAccessResponse {
  isSuccess: boolean;
  message: string;
  httpStatusCode: number;
  data: {
    acknowledged: boolean;
    itemId: string;
    totalImpactedData: number;
  };
  errors: unknown[];
}

export interface IExecuteGraphQLPayload {
  projectShortKey: string;
  query: string;
}

export interface IMockDataResponse {
  errors: unknown | null;
  isSuccess: boolean;
  data: {
    items: Array<{
      collectionName: string;
      schemaName: string;
      count: number;
    }>;
  };
}

export interface IDeleteMockDataPayload {
  projectKey: string;
  schemaNames: Array<string>;
}

export interface IDeleteMockDataResponse extends ISetDataAccessResponse {}

/** Policy rule within a rule group */
export interface IPolicyRule {
  leftSource: number;
  leftOperand: string;
  operator: number;
  rightSource: number;
  rightOperand: string | string[];
  staticValue: string | string[] | null;
  description?: string;
}

/** A group of rules with a logical operator */
export interface IPolicyRuleGroup {
  logicalOperator: number;
  rules: IPolicyRule[];
  nestedGroups: IPolicyRuleGroup[];
}

/** Payload for creating a data-access policy */
export interface ICreatePolicyPayload {
  policyName: string;
  policyDescription?: string;
  policyType: number;
  operation: number;
  schemaName: string;
  schemaId: string;
  fieldNames: string[];
  ruleGroup: IPolicyRuleGroup;
  priority: number;
  isAllowPolicy: boolean;
  projectKey: string;
}

export interface ICreatePolicyData {
  acknowledged: boolean;
  itemId: string;
  totalImpactedData: number;
  message: string | null;
}

export interface ICreatePolicyResponse {
  isSuccess: boolean;
  message: string;
  httpStatusCode: number;
  data: ICreatePolicyData;
  errors: string[];
}

/** Single policy item returned from getPolicy */
export interface IPolicyItem {
  itemId?: string;
  policyName: string;
  policyDescription?: string;
  policyType: number;
  operation: number;
  entityName: string;
  schemaId: string;
  fieldNames: string[];
  ruleGroup: IPolicyRuleGroup;
  priority: number;
  isAllowPolicy: boolean;
  projectKey: string;
}

/** Payload for updating a data-access policy */
export interface IUpdatePolicyPayload extends ICreatePolicyPayload {
  itemId: string;
}

/** Response from getPolicy API */
export interface IGetPolicyResponse {
  isSuccess: boolean;
  message: string;
  httpStatusCode: number;
  data: IPolicyItem[];
  errors: string[];
}

export interface IDeletePolicyPayload {
  itemId: string;
  projectKey: string;
}

export type IDeletePolicyResponse = ICreatePolicyResponse;

export interface IGetConfigurationPayload {
  projectKey: string;
}

export interface IGetSchemaFieldValidationPayload {
  schemaId: string;
  fieldName: string;
  projectKey: string;
  enabled?: boolean;
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

export interface IGetSchemaFieldValidationResponse extends IDefaultResponse {
  data: ISchemaFieldValidationData | null;
}

export interface ICreateSchemaFieldValidationPayload {
  projectKey: string;
  schemaId: string;
  fieldName: string;
  validations: ISchemaFieldValidation[];
}

export interface IUpdateSchemaFieldValidationPayload {
  projectKey: string;
  itemId: string;
  schemaId: string;
  fieldName: string;
  validations: ISchemaFieldValidation[];
}

export interface IDeleteSchemaFieldValidationPayload {
  id: string;
  projectKey: string;
}

// export interface IDeleteSchemaFieldValidationResponse extends IDefaultResponse {
//   data: null | [];
// }

// ── Schema Export ──────────────────────────────────────────────────────────
/** Mirrors backend `SchemaExportOption`. Value `All` (3) includes both optional sections together. */
export const SchemaExportOption = {
  /** Schema structure and field access levels (Read/Write/Edit/Delete) from schema definition */
  Schema: 0,
  /** Include RLS and CLS policies from data access policy */
  AccessPolicies: 1,
  /** Include per-field validation rules */
  ValidationRules: 2,
  /** Schema plus all optional sections (AccessPolicies + ValidationRules) */
  All: 3,
} as const;

export type SchemaExportOptionValue =
  (typeof SchemaExportOption)[keyof typeof SchemaExportOption];

export interface ISchemaExportPayload {
  projectKey: string;
  messageCoRelationId: string;
  /**
   * Export scope: `Schema` (0) is schema-only; `AccessPolicies` (1) or `ValidationRules` (2) for a single
   * extra section; `All` (3) when both policies and validations are included.
   */
  exportOption: SchemaExportOptionValue;
}

export interface ISchemaExportResponseData {
  acknowledged: boolean;
  itemId: string;
  totalImpactedData: number;
  message: string;
}

export interface ISchemaExportResponse {
  isSuccess: boolean;
  message: string;
  httpStatusCode: number;
  data: ISchemaExportResponseData;
  errors: Array<{
    propertyName: string;
    errorMessage: string;
    attemptedValue: string;
    customState: string;
    severity: number;
    errorCode: string;
    formattedMessagePlaceholderValues: Record<string, string>;
  }>;
}

export type IGetUnAdaptedChangeLogsPayload = IGetConfigurationPayload;
export type IInitiateDataGatewayPipelinePayload = IGetConfigurationPayload;
