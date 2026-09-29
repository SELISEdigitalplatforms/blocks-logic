import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerEmailV1 = () => (
  <GuideContent
    title="Email trigger"
    description="Use this trigger to start a workflow when an inbound email arrives in a configured mailbox."
    steps={[
      "Select the Mailbox the workflow should monitor.",
      "Make sure the mailbox configuration is inbound-enabled, since only inbound configs are listed.",
      "Use a test email to confirm the workflow receives the message data you expect.",
      "Map fields from the trigger output into later nodes as needed.",
    ]}
    notes={[
      "The backend starts workflows only for inbound emails with received status.",
      "The trigger output is the email event data passed into the workflow.",
      "Mailbox options are loaded from the current project.",
      "Selecting a mailbox stores its mail server configuration id and project key for the trigger.",
    ]}
  />
);
