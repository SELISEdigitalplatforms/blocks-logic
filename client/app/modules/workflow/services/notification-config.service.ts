import { serviceInstances } from "@/lib/http-client";
import { NOTIFICATION_CONFIG_ENDPOINTS } from "../constants/notification.endpoint.constant";
import {
  INotificationConfig,
  NotificationRecipients,
  NotificationScope,
} from "../models/notification-config";

/** Audience as Blocks OS stores it: level 0 affected users only, 1 all users, 2 selected. */
interface RawAudience {
  level?: number;
  alsoNamedUsers?: boolean;
}

/** Raw configuration as the OS list returns it. Rows from older servers lack the newer fields. */
interface RawNotificationConfig {
  name?: string;
  description?: string | null;
  isBuiltIn?: boolean;
  isDisabled?: boolean;
  /** 0 project users, 1 console users, 2 both */
  scope?: number;
  audience?: RawAudience | null;
  defaultAudience?: RawAudience | null;
  audienceLocked?: boolean;
  liveEnabled?: boolean;
  pushEnabled?: boolean;
  enablePersistence?: boolean;
}

interface RawNotificationConfigsResponse {
  configurations?: RawNotificationConfig[] | null;
}

const RECIPIENTS_BY_LEVEL: Record<number, NotificationRecipients> = { 0: "owner", 1: "all", 2: "targeted" };
const SCOPE_BY_CODE: Record<number, NotificationScope> = { 0: "project", 1: "console", 2: "both" };

/** The audience the server applies: the tenant's own unless locked, else the default; none means "owner". */
const effectiveAudience = (c: RawNotificationConfig): RawAudience | null =>
  (c.audienceLocked ? null : c.audience) ?? c.defaultAudience ?? null;

export const toNotificationConfig = (c: RawNotificationConfig): INotificationConfig => {
  const audience = effectiveAudience(c);
  const recipients = RECIPIENTS_BY_LEVEL[audience?.level ?? 0] ?? "owner";
  return {
    name: c.name ?? "",
    description: c.description ?? undefined,
    isBuiltIn: !!c.isBuiltIn,
    isDisabled: !!c.isDisabled,
    scope: SCOPE_BY_CODE[c.scope ?? 0] ?? "project",
    recipients,
    alsoNamedUsers: recipients === "targeted" && !!audience?.alsoNamedUsers,
    // A row without liveEnabled predates the channel fields and sends in-app.
    channels: { inApp: c.liveEnabled ?? true, push: !!c.pushEnabled, offline: !!c.enablePersistence },
  };
};

class NotificationConfigService {
  private readonly OsHttpClient = serviceInstances.osService;

  /** Every notification configuration of the current project, from Blocks OS (page size 0 means all). */
  fetchConfigurations = async (): Promise<INotificationConfig[]> => {
    const res = await this.OsHttpClient.get<RawNotificationConfigsResponse | null>(
      `${NOTIFICATION_CONFIG_ENDPOINTS.GET_CONFIGS}?Page=0&PageSize=0`,
      undefined,
      { absoluteUrl: true },
    );
    return (res?.configurations ?? []).filter((c) => !!c.name).map(toNotificationConfig);
  };
}

export const notificationConfigService = new NotificationConfigService();
