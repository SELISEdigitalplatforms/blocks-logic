import { describe, expect, it } from "vitest";
import type { WorkflowNode } from "../models/node.model";
import {
  IAM_USER_AUTHENTICATION_TYPES,
  IAM_USER_OPERATIONS,
  normaliseIamUserParameters,
} from "./iam-user-parameters";
import { upgradeNode } from "./node-upgraders";

const iamNode = (parameters: Record<string, unknown> = {}, extra: Partial<WorkflowNode> = {}): WorkflowNode =>
  ({
    id: "n1",
    name: "IAM User",
    category: "action",
    type: "iamUser",
    version: "v2",
    position: { x: 0, y: 0 },
    data: {},
    parameters: { authenticationType: "clientCredential", clientId: "c1", operation: "getUsers", ...parameters },
    settings: {},
    handle: { sources: [{ key: "source", label: "" }], targets: [{ key: "target", label: "" }] },
    ...extra,
  }) as WorkflowNode;

describe("IAM User enum arrays (H20)", () => {
  it("index equals the server's C# enum value", () => {
    // ActionIamUserV2Parameters.cs: IamUserOperation and IamUserAuthenticationType.
    expect([...IAM_USER_OPERATIONS]).toEqual([
      "getUsers", // GetUsers = 0
      "getUser", // GetUser = 1
      "createUser", // CreateUser = 2
      "updateUser", // UpdateUser = 3
      "activateUser", // ActivateUser = 4
      "deactivateUser", // DeactivateUser = 5
      "assignRoles", // AssignRoles = 6
      "revokeAccess", // RevokeAccess = 7
    ]);
    expect([...IAM_USER_AUTHENTICATION_TYPES]).toEqual(["clientCredential", "blocksAuthentication"]);
  });
});

describe("normaliseIamUserParameters (H20)", () => {
  it.each([
    [2, "createUser"],
    ["2", "createUser"],
    [0, "getUsers"],
    ["7", "revokeAccess"],
  ])("reads operation %j as %s", (stored, name) => {
    expect(normaliseIamUserParameters(iamNode({ operation: stored })).parameters!.operation).toBe(name);
  });

  it.each([
    [1, "blocksAuthentication"],
    ["0", "clientCredential"],
  ])("reads authenticationType %j as %s", (stored, name) => {
    const normalised = normaliseIamUserParameters(iamNode({ authenticationType: stored, operation: 3 }));
    expect(normalised.parameters).toMatchObject({ authenticationType: name, operation: "updateUser", clientId: "c1" });
  });

  it.each([8, -1, 2.5, "8", "-1", " 2", "2.0", "foo", "", "CreateUser", null, undefined, true])(
    "leaves %j unchanged (the server decides)",
    (stored) => {
      const node = iamNode({ operation: stored, authenticationType: stored });
      expect(normaliseIamUserParameters(node)).toBe(node);
    },
  );

  it("returns the same object when already normalised, and never mutates the input", () => {
    const named = iamNode({ operation: "createUser" });
    expect(normaliseIamUserParameters(named)).toBe(named);

    const indexed = iamNode({ operation: 2 });
    const copy = structuredClone(indexed);
    const normalised = normaliseIamUserParameters(indexed);
    expect(indexed).toEqual(copy);
    expect(normalised).not.toBe(indexed);
  });

  it("ignores other node types and nodes without parameters", () => {
    const other = iamNode({ operation: 2 }, { type: "dataAction" });
    expect(normaliseIamUserParameters(other)).toBe(other);
    const bare = iamNode({}, { parameters: undefined });
    expect(normaliseIamUserParameters(bare)).toBe(bare);
  });

  it("upgradeNode applies it to iamUser only, and leaves a current node untouched (nothing to save on open)", () => {
    expect(upgradeNode(iamNode({ operation: 6, authenticationType: 1 })).parameters).toMatchObject({
      operation: "assignRoles",
      authenticationType: "blocksAuthentication",
    });
    const current = iamNode({ operation: "assignRoles" });
    expect(upgradeNode(current)).toBe(current);

    const dataAction = iamNode({ operation: 2 }, { type: "dataAction", parameters: { operation: 2 } });
    expect(upgradeNode(dataAction).parameters).toEqual({ operation: 2 });
  });
});
