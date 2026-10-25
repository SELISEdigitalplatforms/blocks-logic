import { describe, expect, it } from "vitest";
import type { WorkflowNode } from "../models/node.model";
import { hasLegacyClientCredential, upgradeClientCredentialParameters } from "./client-credential-upgrade";
import { upgradeNode } from "./node-upgraders";

const node = (type: string, parameters: Record<string, unknown>): WorkflowNode =>
  ({
    id: "n1",
    name: "Node",
    category: "action",
    type,
    version: "v2",
    position: { x: 0, y: 0 },
    parameters,
  }) as unknown as WorkflowNode;

describe("upgradeClientCredentialParameters", () => {
  it("keeps clientId and drops the legacy composite and secret", () => {
    const out = upgradeClientCredentialParameters(
      node("dataAction", {
        authenticationType: "clientCredential",
        clientCredential_composite: "cc1:::undefined",
        clientId: "cc1",
        clientSecret: "undefined",
      }),
    );
    expect(out.parameters).toEqual({ authenticationType: "clientCredential", clientId: "cc1" });
  });

  it("parses clientId from the composite when it is missing", () => {
    const out = upgradeClientCredentialParameters(
      node("httpRequest", { clientCredential_composite: "cc1:::s1", clientSecret: "s1" }),
    );
    expect(out.parameters).toEqual({ clientId: "cc1" });
  });

  it("returns a node already in the new shape unchanged", () => {
    const current = node("dataAction", { clientId: "cc1" });
    expect(upgradeClientCredentialParameters(current)).toBe(current);
    expect(hasLegacyClientCredential(current)).toBe(false);
  });

  it("leaves other node types alone", () => {
    const other = node("function", { clientSecret: "keep" });
    expect(upgradeClientCredentialParameters(other)).toBe(other);
    expect(hasLegacyClientCredential(other)).toBe(false);
  });

  it("runs as part of upgradeNode on load", () => {
    const out = upgradeNode(node("dataAction", { clientCredential_composite: "cc1:::undefined" }));
    expect(out.parameters).toEqual({ clientId: "cc1" });
  });
});
