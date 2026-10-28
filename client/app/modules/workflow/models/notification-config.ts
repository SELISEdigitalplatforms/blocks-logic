/** Who a notification configuration sends to. */
export type NotificationReceiver = "none" | "broadcast" | "users" | "filter";

/** A project notification configuration, as the Send Notification node needs it. */
export interface INotificationConfig {
  name: string;
  receiver: NotificationReceiver;
  notifyMethod: string;
  enablePersistence: boolean;
  isBuiltIn: boolean;
  isDisabled: boolean;
  description?: string;
}
