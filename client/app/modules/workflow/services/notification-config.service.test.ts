import { beforeEach, describe, expect, it, vi } from "vitest";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));

vi.mock("@seliseblocks/genesis-os", () => ({ getRuntimeEnv: () => "https://os.example.com" }));

vi.mock("@/lib/http-client", () => ({
  serviceInstances: { osService: { get } },
}));

import { notificationConfigService, toNotificationConfig } from "./notification-config.service";

describe("notificationConfigService", () => {
  beforeEach(() => get.mockReset());

  it("asks the OS host for every configuration, with no extra query", async () => {
    get.mockResolvedValue({ configurations: [] });

    await notificationConfigService.fetchConfigurations();

    expect(get.mock.calls[0][0]).toBe("https://os.example.com/api/Notification/Gets?Page=0&PageSize=0");
    expect(get.mock.calls[0][2]).toEqual({ absoluteUrl: true });
  });

  it("maps rows and drops rows without a name", async () => {
    get.mockResolvedValue({
      configurations: [
        {
          name: "order-shipped",
          scope: 0,
          audience: { level: 2, alsoNamedUsers: true },
          liveEnabled: true,
          pushEnabled: true,
          enablePersistence: true,
        },
        { scope: 1 },
      ],
    });

    const configs = await notificationConfigService.fetchConfigurations();

    expect(configs).toEqual([
      {
        name: "order-shipped",
        description: undefined,
        isBuiltIn: false,
        isDisabled: false,
        scope: "project",
        recipients: "targeted",
        alsoNamedUsers: true,
        channels: { inApp: true, push: true, offline: true },
      },
    ]);
  });

  it("an empty answer is an empty list", async () => {
    get.mockResolvedValue(null);
    await expect(notificationConfigService.fetchConfigurations()).resolves.toEqual([]);
  });

  it("reads the audience the server applies", () => {
    // No audience: the built-in default, else event owner only.
    expect(toNotificationConfig({ name: "a" }).recipients).toBe("owner");
    expect(toNotificationConfig({ name: "a", defaultAudience: { level: 1 } }).recipients).toBe("all");
    expect(toNotificationConfig({ name: "a", audience: { level: 2 }, defaultAudience: { level: 1 } }).recipients).toBe("targeted");
    // Locked: the tenant's own audience does not apply.
    expect(
      toNotificationConfig({ name: "a", audienceLocked: true, audience: { level: 1 }, defaultAudience: { level: 0 } }).recipients,
    ).toBe("owner");
  });

  it("only Targeted keeps the event owner flag", () => {
    expect(toNotificationConfig({ name: "a", audience: { level: 1, alsoNamedUsers: true } }).alsoNamedUsers).toBe(false);
  });

  it("reads scope and channel defaults of older rows", () => {
    const config = toNotificationConfig({ name: "a" });
    expect(config.scope).toBe("project");
    expect(config.channels).toEqual({ inApp: true, push: false, offline: false });
    expect(toNotificationConfig({ name: "a", scope: 1 }).scope).toBe("console");
    expect(toNotificationConfig({ name: "a", scope: 2 }).scope).toBe("both");
    expect(toNotificationConfig({ name: "a", liveEnabled: false }).channels.inApp).toBe(false);
  });
});
