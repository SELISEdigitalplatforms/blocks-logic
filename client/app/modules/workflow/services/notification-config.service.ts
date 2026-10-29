import { getRuntimeEnv } from "@seliseblocks/genesis-os";
import { serviceInstances } from "@/lib/http-client";
import { NOTIFICATION_CONFIG_ENDPOINTS } from "../constants/notification.endpoint.constant";
import { INotificationConfig, NotificationReceiver } from "../models/notification-config";

/** Raw configuration as the API returns it; enums may arrive as numbers or names. */
interface RawNotificationConfig {
  name?: string;
  notificationType?: number | string;
  notifyMethod?: string;
  enablePersistence?: boolean;
  isBuiltIn?: boolean;
  isDisabled?: boolean;
  description?: string | null;
}

interface RawNotificationConfigsResponse {
  configurations?: RawNotificationConfig[] | null;
}

const RECEIVER_BY_CODE: Record<string, NotificationReceiver> = {
  "0": "none",
  NoReceiverType: "none",
  "1": "broadcast",
  BroadcastReceiverType: "broadcast",
  "2": "users",
  UserSpecificReceiverType: "users",
  "3": "filter",
  FilterSpecificReceiverType: "filter",
};

/** `NotificationReceiverTypes` (number or name) to the node's receiver; unknown reads as "none". */
export const toNotificationReceiver = (value: unknown): NotificationReceiver =>
  typeof value === "number" || typeof value === "string"
    ? (RECEIVER_BY_CODE[String(value)] ?? "none")
    : "none";

class NotificationConfigService {
  private readonly OsHttpClient = serviceInstances.osService;

  /** Every notification configuration of the current project (page size 0 means all). */
  fetchConfigurations = async (): Promise<INotificationConfig[]> => {
    const res = await this.OsHttpClient.get<RawNotificationConfigsResponse | null>(
      // TEMP: echoes BLOCKS_OS_BASE_URL to check the runtime value; remove before merge.
      `${NOTIFICATION_CONFIG_ENDPOINTS.GET_CONFIGS}?Page=0&PageSize=0&url=${encodeURIComponent(getRuntimeEnv("BLOCKS_OS_BASE_URL"))}`,
      undefined,
      { absoluteUrl: true },
    );
    return (res?.configurations ?? [])
      .filter((c) => !!c.name)
      .map((c) => ({
        name: c.name ?? "",
        receiver: toNotificationReceiver(c.notificationType),
        notifyMethod: c.notifyMethod ?? "",
        enablePersistence: !!c.enablePersistence,
        isBuiltIn: !!c.isBuiltIn,
        isDisabled: !!c.isDisabled,
        description: c.description ?? undefined,
      }));
  };
}

export const notificationConfigService = new NotificationConfigService();
