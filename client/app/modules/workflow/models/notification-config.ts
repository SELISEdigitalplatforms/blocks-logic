/**
 * Who in the project receives a notification, from the configuration's audience.
 * "owner": only the users the sending step names; "all": every active user of the send's organization;
 * "targeted": the users, roles and permissions picked on the configuration.
 */
export type NotificationRecipients = "owner" | "all" | "targeted";

/** Which user groups a configuration is for: project users, Blocks Console users, or both. */
export type NotificationScope = "project" | "console" | "both";

/** A project notification configuration, as the Send Notification node needs it. */
export interface INotificationConfig {
  name: string;
  description?: string;
  isBuiltIn: boolean;
  isDisabled: boolean;
  scope: NotificationScope;
  recipients: NotificationRecipients;
  /** Targeted only: the users the sending step names are notified too. */
  alsoNamedUsers: boolean;
  channels: { inApp: boolean; push: boolean; offline: boolean };
}
