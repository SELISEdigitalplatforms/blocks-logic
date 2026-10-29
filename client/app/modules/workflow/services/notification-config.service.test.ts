import { beforeEach, describe, expect, it, vi } from "vitest";

const { get } = vi.hoisted(() => ({ get: vi.fn() }));

vi.mock("@seliseblocks/genesis-os", () => ({ getRuntimeEnv: () => "https://os.example.com" }));

vi.mock("@/lib/http-client", () => ({
  serviceInstances: { osService: { get } },
}));

import { notificationConfigService, toNotificationReceiver } from "./notification-config.service";

describe("notificationConfigService", () => {
  beforeEach(() => get.mockReset());

  it("reads receivers sent as numbers or names", () => {
    expect(toNotificationReceiver(0)).toBe("none");
    expect(toNotificationReceiver(1)).toBe("broadcast");
    expect(toNotificationReceiver(2)).toBe("users");
    expect(toNotificationReceiver(3)).toBe("filter");
    expect(toNotificationReceiver("FilterSpecificReceiverType")).toBe("filter");
    expect(toNotificationReceiver(undefined)).toBe("none");
  });

  it("asks for every configuration and maps them", async () => {
    get.mockResolvedValue({
      configurations: [
        { name: "order-shipped", notificationType: 2, notifyMethod: "OrderShipped", enablePersistence: true },
        { name: "logic-x", notificationType: "BroadcastReceiverType", isBuiltIn: true, isDisabled: true },
        { notificationType: 1 },
      ],
    });

    const configs = await notificationConfigService.fetchConfigurations();

    expect(get.mock.calls[0][0]).toMatch(/\/Notification\/Gets\?Page=0&PageSize=0&url=/);
    expect(configs).toEqual([
      {
        name: "order-shipped",
        receiver: "users",
        notifyMethod: "OrderShipped",
        enablePersistence: true,
        isBuiltIn: false,
        isDisabled: false,
        description: undefined,
      },
      {
        name: "logic-x",
        receiver: "broadcast",
        notifyMethod: "",
        enablePersistence: false,
        isBuiltIn: true,
        isDisabled: true,
        description: undefined,
      },
    ]);
  });

  it("an empty answer is an empty list", async () => {
    get.mockResolvedValue(null);
    expect(await notificationConfigService.fetchConfigurations()).toEqual([]);
  });
});
