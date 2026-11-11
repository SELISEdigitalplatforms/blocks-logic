import { GuideContent } from "./node-guide-content";

export const NodeGuideActionNotification = () => (
  <GuideContent
    title="Send Notification action"
    description="Use this node to send a Blocks notification through one of the project's notification configurations. The configuration, managed in Blocks OS → Notification, decides who receives it and on which channels: in-app, mobile push and offline."
    steps={[
      "Pick a Configuration. The line under each one shows its recipients and channels; built-in configurations are marked.",
      "Organization ID: project users are always matched inside one organization. Leave it empty for the default organization. It is hidden for configurations that go only to Console users.",
      "User IDs name the users the event is about. They are used only when the configuration is Event owner only (required: only these users receive it), Targeted users + event owner (added to the configuration's users), or for Console users. For All users and plain Targeted configurations they would be ignored, so the field is hidden.",
      "Optionally set a Response key and value so the receiving page can match the notification.",
      "Write the Payload as JSON, for example { \"title\": \"Order {{$json.id}} shipped\", \"description\": \"...\" }. Mobile push text placeholders read its fields.",
      "Test with sample data before enabling it in a live path.",
    ]}
    notes={[
      "The node queues one notification for each input item; delivery happens right after, in the background.",
      "The output includes Queued, NotificationId, the configuration's recipients and channels, a count of named users (not the users themselves) and NamedUsersUsed, which says whether the configuration used them.",
      "An inactive configuration is not sent: the output says Skipped: disabled.",
      "An unknown configuration, one with no channel on, or a missing user id becomes an error item with the reason.",
      "A configuration's rate limit (120 per minute unless set) drops anything above it; the step log warns when one step goes over it. Built-in configurations share that limit with the platform's own notifications.",
      "A payload that is not valid JSON after expressions are filled fails the step.",
      "Roles and subscription filters that choose receivers are set on the configuration only; a step cannot add or change them.",
      "If no configuration is listed, the project has none yet, or you lack permission to read them.",
    ]}
  />
);
