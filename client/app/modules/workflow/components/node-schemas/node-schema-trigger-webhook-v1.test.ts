import { describe, expect, it, vi } from "vitest";
import {
  NodeSchemaTriggerWebhookV1,
  REQUEST_FORMAT_JSON_LEGACY,
  REQUEST_FORMAT_STANDARD,
} from "./node-schema-trigger-webhook-v1";

vi.mock("../../services/iam.service", () => ({ iamService: {} }));

const fields = NodeSchemaTriggerWebhookV1.schema.parameters;
const byKey = (key: string) => {
  const found = fields.find((f) => f.key === key);
  if (!found) throw new Error(`no field ${key}`);
  return found;
};
const RETIRED_KEYS = [
  "bodyMode",
  "acceptedBodyTypes",
  "filesFolderId",
  "storageConfigurationName",
  "allowedHeaders",
];
const isHidden = (key: string, data: Record<string, unknown>) => {
  const hidden = byKey(key).hidden;
  return typeof hidden === "function" ? hidden(data) : Boolean(hidden);
};

describe("webhook trigger v1 request format", () => {
  it("starts new webhook nodes on Standard", () => {
    const defaults = NodeSchemaTriggerWebhookV1.defaults.parameters;
    expect(defaults).toMatchObject({ requestFormat: REQUEST_FORMAT_STANDARD });
    for (const retired of RETIRED_KEYS) {
      expect(defaults).not.toHaveProperty(retired);
    }
  });

  it("has no body mode, accepted types, storage or header fields", () => {
    const keys = fields.map((f) => f.key);
    for (const retired of RETIRED_KEYS) {
      expect(keys).not.toContain(retired);
    }
  });

  it("offers Standard and JSON only (legacy), and shows a node without requestFormat as legacy", () => {
    const format = byKey("requestFormat");
    expect(format.type).toBe("select-with-description");
    expect(format.label).toBe("Request Format");
    expect(format.defaultValue).toBe(REQUEST_FORMAT_JSON_LEGACY);
    expect(
      (format.options as { value: string; label: string; description: string }[]).map((o) => [
        o.value,
        o.label,
        o.description,
      ]),
    ).toEqual([
      ["standard", "Standard", "Any body type."],
      ["jsonLegacy", "JSON only (legacy)", "JSON object or array of objects."],
    ]);
  });

  it("shows Request Format on every node, so new and legacy webhooks can switch either way", () => {
    expect(isHidden("requestFormat", {})).toBe(false);
    expect(isHidden("requestFormat", { requestFormat: REQUEST_FORMAT_JSON_LEGACY })).toBe(false);
    expect(isHidden("requestFormat", { requestFormat: REQUEST_FORMAT_STANDARD })).toBe(false);
    // A dependency on its own key would be reset by the form builder on every change.
    expect(byKey("requestFormat").dependsOn).toBeUndefined();
  });

  it("places Request Format between HTTP Method and Authentication Type", () => {
    const ids = fields.map((f) => f.id);
    const from = ids.indexOf("http-method");
    expect(ids.slice(from, from + 3)).toEqual(["http-method", "request-format", "auth-type"]);
  });

  it("does not add requestFormat to an existing node when it is saved", () => {
    const legacy = {
      id: "hook",
      type: "webhook",
      parameters: { authType: "none", httpResponseMode: "immediate" },
    } as unknown as Parameters<NonNullable<typeof NodeSchemaTriggerWebhookV1.transform>>[0];

    const out = NodeSchemaTriggerWebhookV1.transform?.(legacy) as { parameters: Record<string, unknown> };

    expect(out.parameters).not.toHaveProperty("requestFormat");
  });
});
