import { describe, expect, it, vi } from "vitest";
import { isDependencySatisfied } from "../node-inspector/form-builder/utils/dependency-resolver";
import {
  BODY_MODE_ANY_CONTENT,
  BODY_MODE_JSON,
  NodeSchemaTriggerWebhookV1,
} from "./node-schema-trigger-webhook-v1";

vi.mock("../../services/iam.service", () => ({ iamService: {} }));

const fields = NodeSchemaTriggerWebhookV1.schema.parameters;
const byKey = (key: string) => {
  const found = fields.find((f) => f.key === key);
  if (!found) throw new Error(`no field ${key}`);
  return found;
};
const ANY_CONTENT_KEYS = ["filesFolderId", "storageConfigurationName", "allowedHeaders"];

// SPEC8 H2/H3: Body Mode and the Any-content fields of the webhook trigger.
describe("webhook trigger v1 body mode", () => {
  it("starts new webhook nodes on Any content with empty file settings", () => {
    expect(NodeSchemaTriggerWebhookV1.defaults.parameters).toMatchObject({
      bodyMode: BODY_MODE_ANY_CONTENT,
      filesFolderId: "",
      storageConfigurationName: "",
      allowedHeaders: [],
    });
  });

  it("shows a saved node without bodyMode as JSON (legacy)", () => {
    const bodyMode = byKey("bodyMode");
    expect(bodyMode.type).toBe("select-with-description");
    expect(bodyMode.defaultValue).toBe(BODY_MODE_JSON);
    expect((bodyMode.options as { value: string; label: string }[]).map((o) => [o.value, o.label])).toEqual([
      ["json", "JSON (legacy)"],
      ["anyContent", "Any content"],
    ]);
  });

  it.each(ANY_CONTENT_KEYS)("%s is visible only in Any content mode", (key) => {
    const field = byKey(key);
    expect(isDependencySatisfied(field, {})).toBe(false);
    expect(isDependencySatisfied(field, { bodyMode: BODY_MODE_JSON })).toBe(false);
    expect(isDependencySatisfied(field, { bodyMode: BODY_MODE_ANY_CONTENT })).toBe(true);
  });

  it("places Body Mode and its fields between HTTP Method and Authentication Type", () => {
    const ids = fields.map((f) => f.id);
    const from = ids.indexOf("http-method");
    expect(ids.slice(from, from + 6)).toEqual([
      "http-method",
      "body-mode",
      "files-folder-id",
      "storage-configuration-name",
      "allowed-headers",
      "auth-type",
    ]);
  });

  it("uses a comma list for allowed headers, saved as an array", () => {
    expect(byKey("allowedHeaders").type).toBe("comma-list");
    expect(byKey("allowedHeaders").defaultValue).toEqual([]);
  });

  it("does not add bodyMode to an existing node when it is saved", () => {
    const legacy = {
      id: "hook",
      type: "webhook",
      parameters: { authType: "none", httpResponseMode: "immediate" },
    } as unknown as Parameters<NonNullable<typeof NodeSchemaTriggerWebhookV1.transform>>[0];

    const out = NodeSchemaTriggerWebhookV1.transform?.(legacy) as { parameters: Record<string, unknown> };

    expect(out.parameters).not.toHaveProperty("bodyMode");
  });
});
