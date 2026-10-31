import { beforeEach, describe, expect, it, vi } from "vitest";

const { fetchConfigurations } = vi.hoisted(() => ({ fetchConfigurations: vi.fn() }));

vi.mock("@blocks-workflow/services/notification-config.service", () => ({
  notificationConfigService: { fetchConfigurations },
}));

import {
  NodeSchemaActionNotification,
  buildNotificationConfigDetails,
  clearNotificationConfigCache,
  userIdsUse,
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

  it("lists the project's configurations without the built-in ones", async () => {
    fetchConfigurations.mockResolvedValue([
      config(),
      config({ name: "logic-workflow-run-failed", isBuiltIn: true }),
      config({ name: "everyone", recipients: "all", isDisabled: true, channels: { inApp: true, push: true, offline: false } }),
    ]);

    const options = await field("ConfigurationName").options({}, cfg);

    expect(options.map((o: AnyRec) => o.value)).toEqual(["order-shipped", "everyone"]);
    expect(options[0].description).toBe("Event owner only · In-app, Offline");
    expect(options[1].description).toBe("All users · In-app, Mobile push · inactive, the step will skip it");
  });

  it("an empty picker when the list cannot be loaded", async () => {
    fetchConfigurations.mockRejectedValue(new Error("403"));
    await expect(field("ConfigurationName").options({}, cfg)).resolves.toEqual([]);
  });

  it("picking a configuration stores what the form needs to show its fields", async () => {
    fetchConfigurations.mockResolvedValue([config({ name: "t", recipients: "targeted", alsoNamedUsers: true, scope: "both" })]);
    await field("ConfigurationName").options({}, cfg);

    expect(field("ConfigurationName").onChange("t")).toEqual({
      ConfigurationName: "t",
      Recipients: "targeted",
      AlsoNamedUsers: true,
      Scope: "both",
    });
  });

  it("asks for user ids only when the configuration uses them", () => {
    expect(userIdsUse({ Recipients: "owner", Scope: "project" })).toBe("required");
    expect(userIdsUse({ Recipients: "all", Scope: "console" })).toBe("required");
    expect(userIdsUse({ Recipients: "all", Scope: "both" })).toBe("optional");
    expect(userIdsUse({ Recipients: "targeted", AlsoNamedUsers: true, Scope: "project" })).toBe("optional");
    expect(userIdsUse({ Recipients: "targeted", AlsoNamedUsers: false, Scope: "project" })).toBe("unused");
    expect(userIdsUse({ Recipients: "all", Scope: "project" })).toBe("unused");

    const userIds = field("UserIds");
    const owner = { ConfigurationName: "x", Recipients: "owner", Scope: "project" };
    const all = { ConfigurationName: "x", Recipients: "all", Scope: "project" };
    expect(userIds.required(owner)).toBe(true);
    expect(userIds.hidden(owner)).toBe(false);
    expect(userIds.hidden(all)).toBe(true);
    expect(field("userIdsUnused").hidden(all)).toBe(false);
  });

  it("shows the organization only for All users and Targeted", () => {
    const organization = field("OrganizationId");
    expect(organization.hidden({ Recipients: "owner" })).toBe(true);
    expect(organization.hidden({ Recipients: "all" })).toBe(false);
    expect(organization.hidden({ Recipients: "targeted" })).toBe(false);
  });

  it("keeps SubscriptionFilters in step with the filter fields", () => {
    expect(field("FilterContext").onChange("order", { FilterValue: "7" })).toEqual({
      SubscriptionFilters: [{ Context: "order", ActionName: "", Value: "7" }],
    });
    expect(field("FilterValue").onChange("7", { FilterContext: "" })).toEqual({ SubscriptionFilters: [] });
  });

  it("defaults to an empty title and description payload and no removed v1 keys", () => {
    const parameters = NodeSchemaActionNotification.defaults.parameters as AnyRec;
    expect(JSON.parse(parameters.Payload)).toEqual({ title: "", description: "" });
    expect(parameters).not.toHaveProperty("Roles");
    expect(parameters).not.toHaveProperty("SaveAsObject");
    expect(parameters).not.toHaveProperty("NotificationType");
  });

  it("describes the picked configuration and warns when it will not send", () => {
    expect(buildNotificationConfigDetails(undefined).message).toMatch(/no longer exists/);
    expect(buildNotificationConfigDetails(config({ isBuiltIn: true })).message).toMatch(/Built-in/);

    const details = buildNotificationConfigDetails(config({ recipients: "targeted", alsoNamedUsers: true }));
    expect(details.fields.map((f) => f.value)).toEqual([
      "Project users",
      "Targeted users + event owner",
      "In-app, Offline",
    ]);
    expect(details.message).toBeUndefined();

    expect(buildNotificationConfigDetails(config({ isDisabled: true })).message).toMatch(/inactive/);
    expect(
      buildNotificationConfigDetails(config({ channels: { inApp: false, push: false, offline: false } })).message,
    ).toMatch(/no delivery channel/);
  });
});
