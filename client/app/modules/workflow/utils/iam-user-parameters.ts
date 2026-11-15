import type { WorkflowNode } from "../models/node.model";

/**
 * The IAM User node's operations, in the order of the server's `IamUserOperation` enum: the array index is
 * the C# value. The indexes are a contract, so entries are never reordered or removed, only appended.
 */
export const IAM_USER_OPERATIONS = [
  "getUsers",
  "getUser",
  "createUser",
  "updateUser",
  "activateUser",
  "deactivateUser",
  "assignRoles",
  "revokeAccess",
] as const;

/** The server's `IamUserAuthenticationType` enum, in order; same contract as {@link IAM_USER_OPERATIONS}. */
export const IAM_USER_AUTHENTICATION_TYPES = ["clientCredential", "blocksAuthentication"] as const;

export type IamUserOperation = (typeof IAM_USER_OPERATIONS)[number];
export type IamUserAuthenticationType = (typeof IAM_USER_AUTHENTICATION_TYPES)[number];

const DIGITS = /^\d+$/;

/** The name at a whole-number index, or at numeric text such as "2"; undefined for anything else. */
const nameAtIndex = (names: readonly string[], value: unknown): string | undefined => {
  let index: number | undefined;
  if (typeof value === "number" && Number.isInteger(value)) index = value;
  else if (typeof value === "string" && DIGITS.test(value)) index = Number(value);
  return index === undefined ? undefined : names[index];
};

/**
 * Reads an `iamUser` node whose `operation` or `authenticationType` was saved as its enum index (the server
 * accepts both) as the matching name, so the select, `dependsOn` and the rows work. In memory only and pure:
 * any other node, a name, unknown text or an index outside the arrays comes back unchanged (the same object
 * when nothing changes); the server then decides. The editor itself always writes names.
 */
export const normaliseIamUserParameters = <T extends WorkflowNode>(node: T): T => {
  if (node.type !== "iamUser" || !node.parameters) return node;

  const operation = nameAtIndex(IAM_USER_OPERATIONS, node.parameters.operation);
  const authenticationType = nameAtIndex(IAM_USER_AUTHENTICATION_TYPES, node.parameters.authenticationType);
  if (operation === undefined && authenticationType === undefined) return node;

  return {
    ...node,
    parameters: {
      ...node.parameters,
      ...(operation === undefined ? {} : { operation }),
      ...(authenticationType === undefined ? {} : { authenticationType }),
    },
  };
};
