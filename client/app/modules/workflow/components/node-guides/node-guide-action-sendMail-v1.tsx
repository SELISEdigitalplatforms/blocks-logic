import { GuideContent } from "./node-guide-content";

export const NodeGuideActionSendMailV1 = () => (
  <GuideContent
    title="Send Mail action"
    description="Use this node to send an email from a project email template. The node asks for the template, language, recipient, and values for any dynamic template keys."
    steps={[
      "Select the Template you want to send.",
      "Choose the Language version of that template.",
      "Enter the recipient in To Email. You can use an expression when the recipient comes from the input item.",
      "Use Map (Body) to provide values for the dynamic keys found in the selected template body. Mapped values can also use expressions.",
      "Test with a safe recipient or sample data before enabling it in a live path.",
    ]}
    notes={[
      "The node sends one email for each input item.",
      "The output includes Success, Errors, and To so later nodes can inspect the send result.",
      "Template and language options are loaded from the current project.",
      "The body mapping keys are derived from the selected template body, so changing the template can change which mappings are shown.",
    ]}
  />
);
