import { tStatic } from "@seliseblocks/genesis-os";
import { NodeGuideActionIamUserV2 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import type {
  JsonFieldGroup,
  JsonFieldRow,
  SearchOptionsPage,
  SearchOptionsRequest,
  SelectOption,
} from "../node-inspector/form-builder/form-field.types";
import { authClientService, iamService } from "../../services/iam.service";
import {
  IAM_USER_AUTHENTICATION_TYPES,
  IAM_USER_OPERATIONS,
  type IamUserAuthenticationType,
  type IamUserOperation,
} from "../../utils/iam-user-parameters";

const LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE = "logic.workflow.nodes.schemas";
const PREFIX = "NODE_SCHEMA_ACTION_IAM_USER_V2";

const text = (key: string, values?: Record<string, string>) =>
  tStatic(LOGIC_WORKFLOW_NODES_SCHEMAS_MODULE, `${PREFIX}.${key}`, values);

const AUTHENTICATION_LABEL_KEYS: Record<IamUserAuthenticationType, string> = {
  clientCredential: "CLIENT_CREDENTIAL",
  blocksAuthentication: "BLOCKS_AUTHENTICATION",
};

const OPERATION_LABEL_KEYS: Record<IamUserOperation, string> = {
  getUsers: "GET_USERS",
  getUser: "GET_USER",
  createUser: "CREATE_USER",
  updateUser: "UPDATE_USER",
  activateUser: "ACTIVATE_USER",
  deactivateUser: "DEACTIVATE_USER",
  assignRoles: "ASSIGN_ROLES",
  revokeAccess: "REVOKE_ACCESS",
};

/** IAM's account states, shown as IAM's own codes. */
export const IAM_ACCOUNT_STATES = ["Active", "PendingVerification", "Suspended", "Deactivated", "LockedOut"] as const;

/** §6 defaults of every key the Fields block edits, as a fresh object so no two nodes share a list. */
export const iamUserOperationDefaults = (): Record<string, unknown> => ({
  organizationId: "",
  userId: "",
  email: "",
  firstName: "",
  lastName: "",
  userName: "",
  phoneNumber: "",
  language: "",
  roles: [],
  sendActivationMail: true,
  notifyUser: true,
  reason: "",
  searchName: "",
  searchEmail: "",
  accountStates: [],
  filterRoles: [],
  page: 0,
  pageSize: 10,
  roleMode: "add",
});

/** §6 defaults of the whole node. No base URL: IAM's address is a server setting. */
const iamUserDefaults = (): Record<string, unknown> => ({
  authenticationType: "clientCredential",
  clientId: "",
  operation: "getUsers",
  ...iamUserOperationDefaults(),
});

/**
 * The role picker's page request. `organizationId` is sent only when the parameter is a non-blank literal;
 * otherwise (blank or an expression) IAM lists the editor user's organization roles.
 */
export const searchIamRoles = (
  request: SearchOptionsRequest,
  data: Record<string, unknown>,
): Promise<SearchOptionsPage> => {
  const organizationId = typeof data.organizationId === "string" ? data.organizationId.trim() : "";
  const literal = organizationId !== "" && !organizationId.includes("{{");
  return iamService
    .getRoles({
      search: request.search,
      page: request.page,
      pageSize: request.pageSize,
      ...(literal ? { organizationId } : {}),
    })
    .then((res) => ({
      options: (res.data ?? []).map((role) => ({ label: role.name || role.slug, value: role.slug })),
      totalCount: res.totalCount ?? 0,
    }));
};

const roleRow = (key: string, label: string, required: boolean): JsonFieldRow => ({
  key,
  label,
  kind: "list",
  defaultValue: [],
  required,
  get info() {
    return text("ROLES_INFO");
  },
  searchOptions: searchIamRoles,
  optionsDependencies: ["organizationId"],
  allowCustom: true,
});

const textRow = (key: string, extra: Partial<JsonFieldRow> = {}): JsonFieldRow => ({
  key,
  kind: "text",
  defaultValue: "",
  ...extra,
});

const userIdRow = () => textRow("userId", { required: true });

const organizationRow = () => textRow("organizationId", { info: text("ORGANIZATION_ID_INFO") });

const notifyRow = (infoKey: string): JsonFieldRow => ({
  key: "notifyUser",
  kind: "bool",
  defaultValue: true,
  info: text(infoKey),
});

/** Update: a blank text row keeps the current value. */
const updateTextRow = (key: string, optional: boolean) =>
  textRow(key, { optional, info: text("UPDATE_TEXT_INFO") });

const accountStateOptions = (): SelectOption[] => IAM_ACCOUNT_STATES.map((state) => ({ label: state, value: state }));

/**
 * The Fields block's rows for an operation (§4). `context.isMultiOrgEnabled` comes from the organization
 * settings: anything but `false` (not loaded yet, failed, or true) marks Create's organization required.
 */
export const iamUserRows = (
  data: Record<string, unknown>,
  context: Record<string, unknown>,
): Array<JsonFieldRow | JsonFieldGroup> => {
  switch (data.operation) {
    case "createUser":
      return [
        textRow("organizationId", {
          required: context.isMultiOrgEnabled !== false,
          info: text("ORGANIZATION_ID_CREATE_INFO"),
        }),
        textRow("email", { required: true, comment: { text: text("EXISTING_EMAIL_COMMENT") } }),
        textRow("firstName"),
        textRow("lastName"),
        textRow("userName", { optional: true }),
        textRow("phoneNumber", { optional: true }),
        textRow("language", { optional: true }),
        roleRow("roles", "roles", false),
        { key: "sendActivationMail", kind: "bool", defaultValue: true, info: text("SEND_ACTIVATION_MAIL_INFO") },
        notifyRow("NOTIFY_USER_CREATE_INFO"),
      ];
    case "updateUser":
      return [
        userIdRow(),
        organizationRow(),
        updateTextRow("firstName", false),
        updateTextRow("lastName", true),
        updateTextRow("phoneNumber", true),
        updateTextRow("language", true),
      ];
    case "activateUser":
      return [userIdRow(), textRow("reason", { required: true, info: text("REASON_INFO") })];
    case "deactivateUser":
      return [userIdRow()];
    case "getUser":
      return [userIdRow(), organizationRow()];
    case "assignRoles":
      return [
        userIdRow(),
        organizationRow(),
        {
          key: "roleMode",
          kind: "enum",
          defaultValue: "add",
          required: true,
          options: [
            { value: "add", label: text("ROLE_MODE_ADD") },
            { value: "replace", label: text("ROLE_MODE_REPLACE") },
          ],
          comment: { text: text("REPLACE_COMMENT"), tone: "warn", when: (current) => current.roleMode === "replace" },
        },
        {
          ...roleRow("roles", "roles", true),
          comment: { text: text("ADD_COMMENT"), when: (current) => current.roleMode === "add" },
        },
        notifyRow("NOTIFY_USER_ASSIGN_INFO"),
      ];
    case "revokeAccess":
      return [userIdRow(), organizationRow(), notifyRow("NOTIFY_USER_REVOKE_INFO")];
    case "getUsers":
      return [
        organizationRow(),
        {
          group: "filter",
          rows: [
            textRow("searchName", { label: "name" }),
            textRow("searchEmail", { label: "email" }),
            { key: "accountStates", kind: "list", defaultValue: [], options: accountStateOptions() },
            roleRow("filterRoles", "roles", false),
          ],
        },
        { key: "page", kind: "int", defaultValue: 0, info: text("PAGE_INFO") },
        { key: "pageSize", kind: "int", defaultValue: 10, info: text("PAGE_SIZE_INFO") },
      ];
    default:
      // An unknown saved operation shows no rows; the server fails the node.
      return [];
  }
};

/** The organization settings for the Create marker; a failed read leaves `{}` (treated as multi-org). */
const loadOrganizationSettings = (): Promise<Record<string, unknown>> =>
  iamService.getOrganizationConfig().then((config) => ({ isMultiOrgEnabled: config?.isMultiOrgEnabled }));

export const NodeSchemaActionIamUserV2: NodeSchemaDefinition = {
  guide: NodeGuideActionIamUserV2,
  schema: {
    type: "iamUser",
    category: "action",
    version: "v2",
    parameters: [
      {
        id: "authenticationType",
        type: "select",
        get label() {
          return text("AUTHENTICATION");
        },
        get info() {
          return text("AUTHENTICATION_INFO");
        },
        key: "authenticationType",
        required: true,
        defaultValue: "clientCredential",
        // No "None": every IAM request carries a token.
        options: IAM_USER_AUTHENTICATION_TYPES.map((value) => ({
          value,
          get label() {
            return text(AUTHENTICATION_LABEL_KEYS[value]);
          },
        })),
      },
      {
        id: "clientId",
        type: "searchable-select",
        get label() {
          return text("CLIENT_CREDENTIAL");
        },
        get info() {
          return text("CLIENT_CREDENTIAL_INFO");
        },
        key: "clientId",
        required: true,
        defaultValue: "",
        options: () =>
          authClientService.clients.getClientCredentials().then((res) =>
            res
              .filter((item) => item.isActive)
              .map((item) => ({
                value: item.itemId,
                label: item.name,
              })),
          ),
        unmatchedLabel: (value) => text("NOT_FOUND_OR_INACTIVE", { value }),
        dependsOn: {
          key: "authenticationType",
          value: "clientCredential",
          operator: "equals",
        },
      },
      {
        id: "blocksAuthenticationNotes",
        type: "callout-accordion-display",
        key: "blocksAuthenticationNotes",
        transient: true,
        displayValue: () => ({
          title: text("BLOCKS_AUTHENTICATION_CALLOUT_TITLE"),
          description: text("BLOCKS_AUTHENTICATION_CALLOUT"),
        }),
        dependsOn: {
          key: "authenticationType",
          value: "blocksAuthentication",
          operator: "equals",
        },
      },
      {
        id: "operation",
        type: "select",
        get label() {
          return text("OPERATION");
        },
        get info() {
          return text("OPERATION_INFO");
        },
        key: "operation",
        required: true,
        defaultValue: "getUsers",
        options: IAM_USER_OPERATIONS.map((value) => ({
          value,
          get label() {
            return text(OPERATION_LABEL_KEYS[value]);
          },
        })),
        // Every operation field goes back to its default, never null (C21).
        onChange: () => iamUserOperationDefaults(),
      },
      {
        id: "fields",
        type: "json-fields",
        get label() {
          return text("FIELDS");
        },
        key: "fields",
        transient: true,
        rows: iamUserRows,
        rowsContext: loadOrganizationSettings,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: iamUserDefaults(),
    settings: {},
  },
  // Identity apart from copying lists, so new nodes never share the defaults' arrays; nothing is stamped.
  transform: (node) => ({
    ...node,
    parameters: Object.fromEntries(
      Object.entries(node.parameters ?? {}).map(([key, value]) => [key, Array.isArray(value) ? [...value] : value]),
    ),
  }),
};
