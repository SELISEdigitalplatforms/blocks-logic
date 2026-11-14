// Import file for the IAM User node spec (blocks-logic#424, phase 2 of the IAM User node; its json-fields
// block is blocks-logic#423). Same file shape as support/filter-fixtures.ts.
//
// The IAM User nodes are not wired to the trigger, so "Execute Step" runs each one once on its own
// (ActionIamUserV2Node: no input and no upstream -> one standalone item) as the signed-in editor user.
// Every node only reads (Get Users) or fails before any request, so nothing changes in IAM.
//
// An imported node skips the editor's transform, which normally fills `apiBaseUrl` from the client's
// BLOCKS_IAM_BASE_URL. The spec therefore reads that value from the running app (readIamBaseUrl) and the
// fixture stamps it on every node, just as the editor would.

import type { Page } from "@playwright/test";
import type { ImportNode, WorkflowImportFile } from "./filter-fixtures";

export const IAM_NODE_NAMES = {
  webhook: "Webhook",
  getUsers: "IAM Get Users",
  pageSize101: "IAM Page Size 101",
  noCredential: "IAM No Credential",
} as const;

/**
 * The IAM base URL the app under test already uses: `BLOCKS_IAM_BASE_URL` from the served
 * runtime-config.js (`window.__BLOCKS_ENV__`). Opens the app first when the page isn't on it yet.
 */
export async function readIamBaseUrl(page: Page): Promise<string> {
  const read = () =>
    page.evaluate(
      () =>
        (window as unknown as { __BLOCKS_ENV__?: Record<string, string> }).__BLOCKS_ENV__
          ?.BLOCKS_IAM_BASE_URL ?? "",
    );
  let value = await read().catch(() => "");
  if (!value) {
    await page.goto("/");
    await page.waitForFunction(
      () => Boolean((window as unknown as { __BLOCKS_ENV__?: unknown }).__BLOCKS_ENV__),
      undefined,
      { timeout: 30_000 },
    );
    value = await read();
  }
  if (!/^https?:\/\//.test(value))
    throw new Error("BLOCKS_IAM_BASE_URL is not set in the app's runtime config");
  return value.replace(/\/+$/, "");
}

/** §6 defaults (client node-schema-action-iamUser-v2.ts iamUserDefaults), with `overrides` applied. */
const iamUserParameters = (
  apiBaseUrl: string,
  overrides: Record<string, unknown>,
): Record<string, unknown> => ({
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
  // Stamped as the editor's transform would (BLOCKS_IAM_BASE_URL), so the server's `IamBaseUrl`
  // fallback isn't needed.
  apiBaseUrl,
  ...overrides,
});

const iamUserNode = (
  apiBaseUrl: string,
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
  parameters: iamUserParameters(apiBaseUrl, overrides),
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
/** `iamBaseUrl`: from readIamBaseUrl, stamped on every IAM User node. */
export function iamUserWorkflowFile(name: string, iamBaseUrl: string): WorkflowImportFile {
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(IDS.webhook),
      iamUserNode(iamBaseUrl, IDS.getUsers, IAM_NODE_NAMES.getUsers, 0, 200, {
        authenticationType: "blocksAuthentication",
        pageSize: 1,
      }),
      iamUserNode(iamBaseUrl, IDS.pageSize101, IAM_NODE_NAMES.pageSize101, 300, 200, {
        authenticationType: "blocksAuthentication",
        pageSize: 101,
      }),
      iamUserNode(iamBaseUrl, IDS.noCredential, IAM_NODE_NAMES.noCredential, 600, 200, {}),
    ],
    edges: [],
  };
}
