import { GuideContent } from "./node-guide-content";

export const NodeGuideActionNotification = () => (
  <GuideContent
    title="Send Notification action"
    description="Use this node to send a Blocks notification through one of the project's notification configurations. The configuration, managed in Blocks OS → Notification, decides who receives it and on which channels: in-app, mobile push and offline."
    steps={[
      "Pick a Configuration. Built-in configurations are not listed.",
      "Check What this configuration does: its audience, recipients and delivery channels.",
      "Add the Users the event is about when the configuration asks for them (Event owner only, or Console users). Each entry can be an expression such as {{$json.userId}}; one that gives a list adds every id in it.",
      "All users and Targeted configurations count only users of one organization. Leave Organization ID empty for the default organization.",
      "Write the Payload as JSON, for example { \"title\": \"Order {{$json.id}} shipped\", \"description\": \"...\" }. Mobile push text placeholders read its fields.",
      "Test with sample data before enabling it in a live path.",
    ]}
    notes={[
      "The node queues one notification for each input item; delivery happens right after, in the background.",
      "The output includes Queued, NotificationId, the configuration's recipients and channels, and a count of named users (not the users themselves).",
      "An inactive configuration is not sent: the output says Skipped: disabled.",
      "An unknown configuration, a built-in one, one with no channel on, or a missing user id becomes an error item with the reason.",
      "A configuration's rate limit (120 per minute unless set) drops anything above it; the step log warns when one step goes over it.",
      "A payload that is not valid JSON after expressions are filled fails the step.",
      "If no configuration is listed, the project has none yet, or you lack permission to read them.",
    ]}
  />
);
