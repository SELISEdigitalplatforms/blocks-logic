import type { WorkflowNode } from "../models/node.model";

/** Node types with a Client Credential option; they store only `clientId`, the server reads the secret. */
const CLIENT_CREDENTIAL_NODE_TYPES: ReadonlySet<string> = new Set(["dataAction", "httpRequest"]);

/** What older editors saved beside `clientId`: an `id:::secret` composite and the secret itself. */
const LEGACY_KEYS = ["clientCredential_composite", "clientSecret"] as const;

const idFromComposite = (composite: unknown): string =>
  typeof composite === "string" ? (composite.split(":::")[0] ?? "").trim() : "";

/** True when a stored Client Credential node still carries the legacy composite or secret. */
export const hasLegacyClientCredential = (node: Pick<WorkflowNode, "type" | "parameters">): boolean =>
  !!node.parameters &&
  CLIENT_CREDENTIAL_NODE_TYPES.has(node.type) &&
  LEGACY_KEYS.some((key) => key in node.parameters!);

/**
 * Moves a Client Credential node to the `clientId`-only shape: `clientId` is kept, or parsed from the
 * legacy composite when missing, and the legacy keys are dropped so no secret is saved again.
 * Pure: a node without legacy keys comes back unchanged (the same object).
 */
export const upgradeClientCredentialParameters = <T extends WorkflowNode>(node: T): T => {
  if (!hasLegacyClientCredential(node)) return node;

  const { clientCredential_composite: composite, clientSecret: _secret, ...rest } = node.parameters!;
  const clientId =
    typeof rest.clientId === "string" && rest.clientId.trim() ? rest.clientId : idFromComposite(composite);
  return { ...node, parameters: { ...rest, clientId } };
};
