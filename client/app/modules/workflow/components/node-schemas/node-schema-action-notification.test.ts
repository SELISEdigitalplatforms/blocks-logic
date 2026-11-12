import { beforeEach, describe, expect, it, vi } from "vitest";

const { fetchConfigurations } = vi.hoisted(() => ({ fetchConfigurations: vi.fn() }));

vi.mock("@blocks-workflow/services/notification-config.service", () => ({
  notificationConfigService: { fetchConfigurations },
}));

import {
  NodeSchemaActionNotification,
  clearNotificationConfigCache,
  organizationIdProps,
  userIdsProps,
} from "./node-schema-action-notification";
import { INotificationConfig } from "../../models/notification-config";

/* eslint-disable @typescript-eslint/no-explicit-any */
type AnyRec = Record<string, any>;

const field = (key: string): AnyRec => {
  const found = NodeSchemaActionNotification.schema.parameters.find((f) => f.key === key);
  if (!found) throw new Error(`no field ${key}`);
  return found as AnyRec;
};

const cfg = { tenantId: "t-1" } as never;

const config = (overrides: Partial<INotificationConfig> = {}): INotificationConfig => ({
  name: "order-shipped",
  isBuiltIn: false,
  isDisabled: false,
  scope: "project",
  recipients: "owner",
  alsoNamedUsers: false,
  channels: { inApp: true, push: false, offline: true },
  ...overrides,
});

describe("action notification v2", () => {
  beforeEach(() => {
    clearNotificationConfigCache();
    fetchConfigurations.mockReset();
  });

  it("is a v2 action of type notification", () => {
    expect(NodeSchemaActionNotification.schema).toMatchObject({
      type: "notification",
      category: "action",
      version: "v2",
    });
  });

  it("lists every configuration of the project, built-in ones marked", async () => {
    fetchConfigurations.mockResolvedValue([
      config(),
      config({ name: "logic-workflow-run-failed", isBuiltIn: true }),
      config({ name: "everyone", recipients: "all", isDisabled: true, channels: { inApp: true, push: true, offline: false } }),
    ]);

    const options = await field("ConfigurationName").options({}, cfg);

    expect(options.map((o: AnyRec) => o.value)).toEqual(["order-shipped", "logic-workflow-run-failed", "everyone"]);
    expect(options[0].description).toBe("Event owner only · In-app, Offline");
    expect(options[1].description).toBe("Built-in · Event owner only · In-app, Offline");
    expect(options[2].description).toBe("All users · In-app, Mobile push · inactive, the step will skip it");
  });

  it("describes targeted configurations that also notify the event owner, and ones with no channel", async () => {
    fetchConfigurations.mockResolvedValue([
      config({ name: "team", recipients: "targeted", alsoNamedUsers: true }),
      config({ name: "team-only", recipients: "targeted" }),
      config({ name: "silent", channels: { inApp: false, push: false, offline: false } }),
    ]);

    const options = await field("ConfigurationName").options({}, cfg);

    expect(options.map((o: AnyRec) => o.description)).toEqual([
      "Targeted users + event owner · In-app, Offline",
      "Targeted users · In-app, Offline",
      "Event owner only · No channel on",
    ]);
  });

  it("an empty picker when the list cannot be loaded", async () => {
    fetchConfigurations.mockRejectedValue(new Error("403"));
    await expect(field("ConfigurationName").options({}, cfg)).resolves.toEqual([]);
  });

  it("picking a configuration saves only its name", () => {
    expect(field("ConfigurationName").onChange).toBeUndefined();
  });

  it("shows the fields in order once a configuration is picked, payload last", () => {
    const keys = NodeSchemaActionNotification.schema.parameters.map((f) => f.key);
    expect(keys).toEqual(["ConfigurationName", "OrganizationId", "UserIds", "ResponseKey", "ResponseValue", "Payload"]);

    for (const key of keys.slice(1)) {
      expect(field(key).hidden({ ConfigurationName: "" })).toBe(true);
      expect(field(key).hidden({ ConfigurationName: "x" })).toBe(false);
      expect(field(key).required).toBeFalsy();
    }
  });

  describe("fields follow the picked configuration", () => {
    it.each([
      ["owner", false, "project", { hidden: false, required: true }, /sends only to these users/],
      ["all", false, "project", { hidden: true }, null],
      ["targeted", false, "project", { hidden: true }, null],
      ["targeted", true, "project", { hidden: false, required: false }, /Added to the users the configuration picks/],
      ["all", false, "console", { hidden: false, required: true }, /Blocks Console users to notify/],
      ["all", false, "both", { hidden: false, required: false }, /Console users among them/],
      ["owner", false, "both", { hidden: false, required: true }, /sends only to these users.*Console users among them/],
    ] as const)("User IDs for %s (also named: %s, scope %s)", (recipients, alsoNamedUsers, scope, expected, info) => {
      const props = userIdsProps(config({ recipients, alsoNamedUsers, scope }));
      expect(props).toMatchObject(expected);
      if (info) expect(props.info).toMatch(info);
    });

    it("Organization ID is hidden only for Console-only configurations and says what it limits", () => {
      expect(organizationIdProps(config({ scope: "console" }))).toEqual({ hidden: true });
      expect(organizationIdProps(config({ recipients: "all" })).info).toMatch(/Every active user of this organization/);
      expect(organizationIdProps(config({ recipients: "owner" })).info).toMatch(/users named below who belong/);
      expect(organizationIdProps(config({ recipients: "targeted", scope: "both" })).info).toMatch(/matched inside this organization/);
    });

    it("an unknown configuration shows both fields with general help", () => {
      expect(userIdsProps()).toMatchObject({ hidden: false, required: false });
      expect(organizationIdProps()).toMatchObject({ hidden: false });
    });

    it("loads the picked configuration from the cached list", async () => {
      fetchConfigurations.mockResolvedValue([config({ name: "everyone", recipients: "all" }), config()]);
      const ctx = { tenantId: "t-1" } as never;

      await expect(field("UserIds").asyncProps({ ConfigurationName: "everyone" }, ctx)).resolves.toEqual({ hidden: true });
      await expect(field("UserIds").asyncProps({ ConfigurationName: "order-shipped" }, ctx)).resolves.toMatchObject({ required: true });
      await expect(field("UserIds").asyncProps({ ConfigurationName: "" }, ctx)).resolves.toEqual({ hidden: true });
      await expect(field("UserIds").asyncProps({ ConfigurationName: "gone" }, ctx)).resolves.toMatchObject({ hidden: false });
      expect(fetchConfigurations).toHaveBeenCalledTimes(1);
      expect(field("UserIds").asyncPropsDependencies).toEqual(["ConfigurationName"]);
      expect(field("OrganizationId").asyncPropsDependencies).toEqual(["ConfigurationName"]);
    });

    it("shows both fields when the list cannot be loaded", async () => {
      fetchConfigurations.mockRejectedValue(new Error("403"));
      await expect(field("OrganizationId").asyncProps({ ConfigurationName: "x" }, cfg)).resolves.toMatchObject({ hidden: false });
    });
  });

  it("saves the node as it is", () => {
    const node = { id: "n-1", parameters: { ConfigurationName: "x" } } as never;
    expect(NodeSchemaActionNotification.transform?.(node)).toBe(node);
  });

  it("defaults to an empty title and description payload and only the saved keys", () => {
    const parameters = NodeSchemaActionNotification.defaults.parameters as AnyRec;
    expect(JSON.parse(parameters.Payload)).toEqual({ title: "", description: "" });
    expect(Object.keys(parameters).sort()).toEqual(
      ["ConfigurationName", "OrganizationId", "Payload", "ResponseKey", "ResponseValue", "UserIds"],
    );
  });
});
