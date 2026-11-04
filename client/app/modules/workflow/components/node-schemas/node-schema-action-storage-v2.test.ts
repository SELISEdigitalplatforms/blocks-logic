import { describe, expect, it, vi } from "vitest";
import { isDependencySatisfied } from "../node-inspector/form-builder/utils/dependency-resolver";
import {
  NodeSchemaActionStorageV2,
  STORAGE_AUTH_BLOCKS,
  STORAGE_AUTH_CLIENT_CREDENTIAL,
  STORAGE_OPERATIONS,
} from "./node-schema-action-storage-v2";
import { NodeSchemasDefinition } from "./node-schemas";
import { NodeDefinitions } from "../node-library-panel/node-definitions";
import schemasEn from "../../../../i18n/logic.workflow.nodes.schemas.en.json";
import guidesEn from "../../../../i18n/logic.workflow.nodes.guides.en.json";
import inspectorEn from "../../../../i18n/logic.workflow.nodes.inspector.en.json";

const { getClientCredentials } = vi.hoisted(() => ({ getClientCredentials: vi.fn() }));
vi.mock("../../services/iam.service", () => ({
  authClientService: { clients: { getClientCredentials } },
  iamService: {},
}));

const fields = NodeSchemaActionStorageV2.schema.parameters;
const byKey = (key: string) => {
  const found = fields.find((f) => f.key === key);
  if (!found) throw new Error(`no field ${key}`);
  return found;
};
const isRequired = (key: string, data: Record<string, unknown>) => {
  const required = byKey(key).required;
  return typeof required === "function" ? required(data) : Boolean(required);
};
const visible = (data: Record<string, unknown>) =>
  fields.filter((f) => !f.dependsOn || isDependencySatisfied(f, data)).map((f) => f.key);

// SPEC11 H1–H3, H13.
describe("Storage node v2 schema", () => {
  it("is registered as a new v2 action type with no v1", () => {
    expect(NodeSchemasDefinition.actionstoragev2).toBe(NodeSchemaActionStorageV2);
    expect(NodeSchemasDefinition.actionstoragev1).toBeUndefined();
    expect(NodeSchemaActionStorageV2.schema).toMatchObject({ type: "storage", category: "action", version: "v2" });
  });

  it("is in the node library with the storage type", () => {
    const entry = NodeDefinitions.find((d) => d.id === "action-storage-v2");
    expect(entry).toMatchObject({ type: "storage", version: "v2", category: "action", title: "Storage" });
    expect(entry?.handle).toEqual({ sources: [{ key: "source", label: "" }], targets: [{ key: "target", label: "" }] });
  });

  it("starts new nodes on Get with Blocks Authentication and nothing filled in", () => {
    expect(NodeSchemaActionStorageV2.defaults.parameters).toEqual({
      operation: "get",
      authenticationType: STORAGE_AUTH_BLOCKS,
      clientId: "",
      fileId: "",
      newName: "",
      permanent: false,
    });
  });

  it("offers the three operations and the two authentication types", () => {
    expect((byKey("operation").options as { value: string }[]).map((o) => o.value)).toEqual([...STORAGE_OPERATIONS]);
    expect((byKey("authenticationType").options as { value: string }[]).map((o) => o.value)).toEqual([
      STORAGE_AUTH_BLOCKS,
      STORAGE_AUTH_CLIENT_CREDENTIAL,
    ]);
  });

  it("shows New Name only for Rename and Delete permanently only for Delete", () => {
    const base = { authenticationType: STORAGE_AUTH_BLOCKS };
    expect(visible({ ...base, operation: "get" })).toEqual(["operation", "authenticationType", "fileId"]);
    expect(visible({ ...base, operation: "rename" })).toEqual(["operation", "authenticationType", "fileId", "newName"]);
    expect(visible({ ...base, operation: "delete" })).toEqual(["operation", "authenticationType", "fileId", "permanent"]);
  });

  it("shows and requires the client picker only for Client Credential", () => {
    expect(visible({ operation: "get", authenticationType: STORAGE_AUTH_CLIENT_CREDENTIAL })).toContain("clientId");
    expect(isRequired("clientId", { authenticationType: STORAGE_AUTH_CLIENT_CREDENTIAL })).toBe(true);
    expect(isRequired("clientId", { authenticationType: STORAGE_AUTH_BLOCKS })).toBe(false);
  });

  it("requires File ID always and New Name only for Rename", () => {
    expect(isRequired("fileId", {})).toBe(true);
    expect(isRequired("newName", { operation: "rename" })).toBe(true);
    expect(isRequired("newName", { operation: "delete" })).toBe(false);
  });

  it("lists only active client credentials, saving the client id", async () => {
    getClientCredentials.mockResolvedValue([
      { itemId: "c1", name: "Scheduler", isActive: true },
      { itemId: "c2", name: "Old", isActive: false },
    ]);
    const options = byKey("clientId").options as (d: unknown, c: unknown) => Promise<unknown>;
    expect(await options({}, { tenantId: "pk" })).toEqual([{ value: "c1", label: "Scheduler" }]);
  });

  it("has an English text for every key it uses", () => {
    const schemaKeys = Object.keys(schemasEn).filter((k) => k.startsWith("NODE_SCHEMA_ACTION_STORAGE_V2."));
    const guideKeys = Object.keys(guidesEn).filter((k) => k.startsWith("NODE_GUIDE_ACTION_STORAGE_V2."));
    expect(schemaKeys.length).toBeGreaterThanOrEqual(16);
    expect(guideKeys).toHaveLength(12);
    expect(inspectorEn).toHaveProperty(["NODE_DEFINITIONS.STORAGE"], "Storage");
    for (const field of fields) {
      expect(field.label).not.toMatch(/^NODE_SCHEMA_/);
      expect(field.info).not.toMatch(/^NODE_SCHEMA_/);
    }
  });
});
