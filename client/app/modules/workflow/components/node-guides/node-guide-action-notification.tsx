import { GuideContent } from "./node-guide-content";

export const NodeGuideActionNotification = () => (
  <GuideContent
    title="Send Notification action"
    description="Use this node to send a Blocks notification through one of the project's notification configurations. The configuration decides who can receive it, the event name browsers listen for, and whether it is saved to the inbox (the bell)."
    steps={[
      "Pick a Configuration. Configurations are managed under Core Services → Notification; built-in ones are not listed.",
      "Check What this configuration does: its receiver, notify method and whether it saves to the inbox.",
      "Users receiver: add User IDs, Roles or Organization IDs. Each entry can be an expression such as {{$json.userId}}; one that gives a list adds every id in it.",
      "Filter receiver: set the Filter context, and optionally the action and value, matching what the receiving page subscribed to.",
      "Broadcast receiver: nothing to add, everyone connected to this project receives it.",
      "Write the Payload as JSON, for example { \"title\": \"Order {{$json.id}} shipped\", \"description\": \"...\" }.",
      "Test with sample data before enabling it in a live path.",
    ]}
    notes={[
      "The node sends one notification for each input item.",
      "The output includes Success, ConfigurationName, NotificationType and recipient counts (not the recipients themselves).",
      "A configuration switched off for the project is not sent: the output says Skipped: disabled.",
      "A send the Notifier refuses (unknown configuration, no recipient, no permission) becomes an error item with the reason.",
      "A payload that is not valid JSON after expressions are filled fails the step.",
      "If no configuration is listed, the project has none yet, or you lack permission to read them.",
    ]}
  />
);
