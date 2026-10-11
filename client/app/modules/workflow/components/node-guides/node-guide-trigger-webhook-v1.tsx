import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerWebhookV1 = () => (
  <GuideContent
    title="Webhook trigger"
    description="Use this trigger to start a workflow when another app sends JSON to the webhook URL. The incoming body becomes the data passed to the next node."
    steps={[
      "Use the Webhook URL: Test while building the workflow, or Production after the workflow is published.",
      "Send a POST request with a JSON body and include the x-blocks-key header. A JSON object creates one workflow item, and a JSON array of objects creates one item per object. Other values (a plain string, number, or an array of them) are rejected. The header name is case-insensitive.",
      "The Deprecated Webhook URL still works and embeds the x-blocks-key value in the path, so it does not need the header. Prefer the Webhook URL for new integrations.",
      "Choose the authentication level: None for open webhooks, Blocks Authentication for signed-in callers, or Blocks Authorization when roles or permissions must be checked.",
      "For Blocks Authorization, choose the organization, authorization mode, and any required roles or permissions.",
      "Choose whether the webhook should respond immediately or wait until the last node finishes.",
    ]}
    notes={[
      "The test URL only works while the workflow is listening for test executions. The production URL requires a published workflow.",
      "Immediate response queues the workflow and returns a status quickly. After Last Node Completion waits for the workflow result.",
      "When waiting for the last node, Response Data controls whether the webhook returns all output items, the first output item, or no body.",
      "Roles and permissions can require all selected values or any selected value, depending on the chosen condition.",
    ]}
  />
);
