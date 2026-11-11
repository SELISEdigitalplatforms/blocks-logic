import { describe, expect, it, vi } from "vitest";
import { isDependencySatisfied } from "../node-inspector/form-builder/utils/dependency-resolver";
import {
  BODY_MODE_ANY_CONTENT,
  BODY_MODE_JSON,
  BODY_TYPES,
  NodeSchemaTriggerWebhookV1,
} from "./node-schema-trigger-webhook-v1";

vi.mock("../../services/iam.service", () => ({ iamService: {} }));

const fields = NodeSchemaTriggerWebhookV1.schema.parameters;
const byKey = (key: string) => {
  const found = fields.find((f) => f.key === key);
  if (!found) throw new Error(`no field ${key}`);
  return found;
};
const isHidden = (key: string, data: Record<string, unknown>) => {
  const hidden = byKey(key).hidden;
  return typeof hidden === "function" ? hidden(data) : Boolean(hidden);
};

// SPEC8 H2/H3 as changed by SPEC10 N1/N8: Body Mode and Accepted Body Types of the webhook trigger.
describe("webhook trigger v1 body settings", () => {
  it("starts new webhook nodes on Any content accepting every body type", () => {
    const defaults = NodeSchemaTriggerWebhookV1.defaults.parameters;
    expect(defaults).toMatchObject({ bodyMode: BODY_MODE_ANY_CONTENT, acceptedBodyTypes: [] });
    for (const retired of ["filesFolderId", "storageConfigurationName", "allowedHeaders"]) {
      expect(defaults).not.toHaveProperty(retired);
    }
  });

  it("has no storage or header fields", () => {
    const keys = fields.map((f) => f.key);
    expect(keys).not.toContain("filesFolderId");
    expect(keys).not.toContain("storageConfigurationName");
    expect(keys).not.toContain("allowedHeaders");
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

  it("shows Body Mode on every node, so new and legacy webhooks can switch either way", () => {
    expect(isHidden("bodyMode", {})).toBe(false);
    expect(isHidden("bodyMode", { bodyMode: BODY_MODE_JSON })).toBe(false);
    expect(isHidden("bodyMode", { bodyMode: BODY_MODE_ANY_CONTENT })).toBe(false);
    // A dependency on its own key would be reset by the form builder on every change.
    expect(byKey("bodyMode").dependsOn).toBeUndefined();
  });

  it("offers Accepted Body Types as a multiselect, only in Any content mode", () => {
    const accepted = byKey("acceptedBodyTypes");
    expect(accepted.type).toBe("multiselect");
    expect(accepted.defaultValue).toEqual([]);
    expect(accepted.placeholder).toBe("All types");
    expect((accepted.options as { value: string; label: string }[]).map((o) => [o.value, o.label])).toEqual([
      ["json", "JSON"],
      ["form", "Form"],
      ["multipart", "Multipart"],
      ["text", "Text"],
      ["binary", "Binary"],
    ]);
    expect(BODY_TYPES).toEqual(["json", "form", "multipart", "text", "binary"]);
    expect(isDependencySatisfied(accepted, {})).toBe(false);
    expect(isDependencySatisfied(accepted, { bodyMode: BODY_MODE_JSON })).toBe(false);
    expect(isDependencySatisfied(accepted, { bodyMode: BODY_MODE_ANY_CONTENT })).toBe(true);
  });

  it("places Body Mode and Accepted Body Types between HTTP Method and Authentication Type", () => {
    const ids = fields.map((f) => f.id);
    const from = ids.indexOf("http-method");
    expect(ids.slice(from, from + 4)).toEqual(["http-method", "body-mode", "accepted-body-types", "auth-type"]);
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
