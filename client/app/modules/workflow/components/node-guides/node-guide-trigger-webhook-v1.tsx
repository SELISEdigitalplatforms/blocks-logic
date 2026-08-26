import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerWebhookV1 = () => (
  <GuideContent
    title="Webhook trigger"
    description="Use this trigger to start a workflow from an HTTP request. The inspector shows separate test and production URLs and lets you configure authentication, authorization, and response behavior."
    steps={[
      "Copy the Webhook URL for Test while building, or Production when the workflow is live.",
      "Send a POST request to the URL; the method is fixed by the schema.",
      "Choose Authentication Type. Use None for open calls, Blocks Authentication for signed-in callers, or Blocks Authorization when roles or permissions should be checked.",
      "For Blocks Authorization, select the organization, authorization mode, and the roles or permissions the caller must satisfy.",
      "Choose whether the webhook responds immediately or after the last node completes. If it waits for the last node, choose the response data shape.",
    ]}
    notes={[
      "The test URL only works while the workflow is listening for test executions. The production URL requires a published workflow.",
      "Immediate response queues the workflow and returns an execution status. After Last Node Completion waits for the run and can return all output items, the first output item, or no body.",
      "Webhook request bodies must be JSON. A JSON array input becomes multiple trigger output items.",
      "Roles and permissions support AND/OR matching through their conditional selectors.",
      "Response Data only appears when the response mode is After Last Node Completion.",
    ]}
  />
);
