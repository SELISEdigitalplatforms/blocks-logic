// Import file for the IAM User node spec (blocks-logic#424, phase 2 of the IAM User node; its json-fields
// block is blocks-logic#423). Same file shape as support/filter-fixtures.ts.
//
// The IAM User nodes are not wired to the trigger, so "Execute Step" runs each one once on its own
// (ActionIamUserV2Node: no input and no upstream -> one standalone item) as the signed-in editor user.
// Every node only reads (Get Users) or fails before any request, so nothing changes in IAM.

import type { ImportNode, WorkflowImportFile } from "./filter-fixtures";

export const IAM_NODE_NAMES = {
  webhook: "Webhook",
  getUsers: "IAM Get Users",
  pageSize101: "IAM Page Size 101",
  noCredential: "IAM No Credential",
} as const;

/** §6 defaults (client node-schema-action-iamUser-v2.ts iamUserDefaults), with `overrides` applied. */
const iamUserParameters = (overrides: Record<string, unknown>): Record<string, unknown> => ({
  authenticationType: "clientCredential",
  clientId: "",
  operation: "getUsers",
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
  // Blank: the server uses its own `IamBaseUrl` setting (the ticket's operations prerequisite).
  apiBaseUrl: "",
  ...overrides,
});

const iamUserNode = (
  id: string,
  name: string,
  x: number,
  y: number,
  overrides: Record<string, unknown>,
): ImportNode => ({
  id,
  name,
  category: "action",
  type: "iamUser",
  version: "v2",
  position: { x, y },
  handle: { sources: [{ key: "source", label: "" }], targets: [{ key: "target", label: "" }] },
  parameters: iamUserParameters(overrides),
  settings: {},
  pinData: null,
});

const webhookNode = (id: string): ImportNode => ({
  id,
  name: IAM_NODE_NAMES.webhook,
  category: "trigger",
  type: "webhook",
  version: "v2",
  position: { x: 0, y: 0 },
  handle: { sources: [{ key: "source", label: "" }], targets: [] },
  parameters: {
    httpMethod: "POST",
    authType: "none",
    httpResponseMode: "immediate",
    httpResponseData: "all",
    organizationId: "",
    roles: { operator: "all", values: [] },
    permissions: { operator: "any", values: [] },
    authorizationMode: "RolesOnly",
    path: id,
  },
  settings: {},
  pinData: null,
});

const IDS = {
  webhook: "e2eiamwebhook00000000000000001",
  getUsers: "e2eiamgetusers0000000000000001",
  pageSize101: "e2eiampagesize000000000000001",
  noCredential: "e2eiamnocred00000000000000001",
};

/**
 * - "IAM Get Users": Blocks Authentication, Get Users, page size 1 (§10 W10 / H13 with a read).
 * - "IAM Page Size 101": the same with page size 101 (§10 W11 -> validation_failed).
 * - "IAM No Credential": Client Credential with no credential selected (§6 clientId rule -> invalid_configuration).
 */
export function iamUserWorkflowFile(name: string): WorkflowImportFile {
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(IDS.webhook),
      iamUserNode(IDS.getUsers, IAM_NODE_NAMES.getUsers, 0, 200, {
        authenticationType: "blocksAuthentication",
        pageSize: 1,
      }),
      iamUserNode(IDS.pageSize101, IAM_NODE_NAMES.pageSize101, 300, 200, {
        authenticationType: "blocksAuthentication",
        pageSize: 101,
      }),
      iamUserNode(IDS.noCredential, IAM_NODE_NAMES.noCredential, 600, 200, {}),
    ],
    edges: [],
  };
}
