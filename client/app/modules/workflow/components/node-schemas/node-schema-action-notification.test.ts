import { beforeEach, describe, expect, it, vi } from "vitest";

const { fetchConfigurations } = vi.hoisted(() => ({ fetchConfigurations: vi.fn() }));

vi.mock("@blocks-workflow/services/notification-config.service", () => ({
  notificationConfigService: { fetchConfigurations },
}));

import {
  NodeSchemaActionNotification,
  buildNotificationConfigDetails,
  clearNotificationConfigCache,
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
  receiver: "users",
  notifyMethod: "OrderShipped",
  enablePersistence: true,
  isBuiltIn: false,
  isDisabled: false,
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
      config({ name: "everyone", receiver: "broadcast", isDisabled: true }),
    ]);

    const options = await field("ConfigurationName").options({}, cfg);

    expect(options.map((o: AnyRec) => o.value)).toEqual(["order-shipped", "everyone"]);
    expect(options[1].description).toContain("switched off");
  });

  it("an empty picker when the configurations cannot be loaded", async () => {
    fetchConfigurations.mockRejectedValue(new Error("403"));

    expect(await field("ConfigurationName").options({}, cfg)).toEqual([]);
  });

  it("picking a configuration stores its receiver", async () => {
    fetchConfigurations.mockResolvedValue([config({ name: "subs", receiver: "filter" })]);
    await field("ConfigurationName").options({}, cfg);

    expect(field("ConfigurationName").onChange("subs", {}, cfg)).toEqual({
      ConfigurationName: "subs",
      NotificationType: "filter",
    });
  });

  it("shows only the recipient fields of the receiver", () => {
    const users = { ConfigurationName: "x", NotificationType: "users" };
    const filter = { ConfigurationName: "x", NotificationType: "filter" };
    const broadcast = { ConfigurationName: "x", NotificationType: "broadcast" };

    for (const key of ["UserIds", "Roles", "OrganizationIds"]) {
      expect(field(key).hidden(users)).toBe(false);
      expect(field(key).hidden(filter)).toBe(true);
      expect(field(key).hidden(broadcast)).toBe(true);
    }
    for (const key of ["FilterContext", "FilterActionName", "FilterValue"]) {
      expect(field(key).hidden(filter)).toBe(false);
      expect(field(key).hidden(users)).toBe(true);
    }
    expect(field("FilterContext").required(filter)).toBe(true);
    // Recipient fields hide instead of depending, so switching configurations keeps what was typed.
    expect(field("UserIds").dependsOn).toBeUndefined();
  });

  it("the filter fields keep SubscriptionFilters as one filter", () => {
    const data = { FilterContext: "order", FilterActionName: "status", FilterValue: "" };

    expect(field("FilterValue").onChange("{{$json.id}}", { ...data, FilterValue: "{{$json.id}}" }, cfg)).toEqual({
      SubscriptionFilters: [{ Context: "order", ActionName: "status", Value: "{{$json.id}}" }],
    });
  });

  it("payload and the rest appear once a configuration is picked", () => {
    expect(field("Payload").hidden({ ConfigurationName: "" })).toBe(true);
    expect(field("Payload").hidden({ ConfigurationName: "x" })).toBe(false);
    expect(field("Payload").type).toBe("json-code-editor");
  });

  it("the details panel is display only and reads the configuration live", async () => {
    fetchConfigurations.mockResolvedValue([config()]);
    const details = field("configDetails");

    expect(details.transient).toBe(true);
    const shown = await details.details({ ConfigurationName: "order-shipped" }, cfg);
    expect(shown.fields.map((f: AnyRec) => f.value)).toEqual([
      "Users — chosen users, roles or organizations",
      "OrderShipped",
      true,
    ]);
  });

  it("the details panel explains missing, built-in, disabled and no-receiver configurations", () => {
    expect(buildNotificationConfigDetails(undefined).message).toContain("no longer exists");
    expect(buildNotificationConfigDetails(config({ isBuiltIn: true })).message).toContain("Built-in");
    expect(buildNotificationConfigDetails(config({ isDisabled: true })).message).toContain("switched off");
    expect(buildNotificationConfigDetails(config({ receiver: "none" })).message).toContain("no receiver");
    expect(buildNotificationConfigDetails(config()).message).toBeUndefined();
  });

  it("defaults to an empty title/description payload and no recipients", () => {
    const params = NodeSchemaActionNotification.defaults.parameters;
    expect(JSON.parse(params.Payload as string)).toEqual({ title: "", description: "" });
    expect(params).toMatchObject({ ConfigurationName: "", UserIds: [], Roles: [], SubscriptionFilters: [] });
  });
});
