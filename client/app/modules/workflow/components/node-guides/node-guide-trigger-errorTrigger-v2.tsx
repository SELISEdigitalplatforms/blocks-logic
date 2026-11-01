import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerErrorTriggerV2 = () => (
  <GuideContent
    title="Error Trigger"
    description="Use this trigger to start this workflow when a run of another workflow fails."
    steps={[
      "Add the Error Trigger and the nodes that should react, for example Send Mail.",
      "Publish this workflow. A production failure runs the published version.",
      "Open the other workflow's ⋮ menu, choose Settings, turn on Alert on failure and select this workflow. Publish it again so production runs use the alert.",
    ]}
    notes={[
      "The output has the failed run in {{ $json.execution }} (workflowId, workflowName, executionId, mode, startedAt, finishedAt and error).",
      "Turn on Run on test mode to also run when a test run (Execute workflow) of the other workflow fails. That uses this workflow's saved draft, and {{ $json.execution.mode }} is \"Test\". A failed Test step never starts it.",
      "Test step on this trigger uses sample data.",
      "A workflow can have one Error Trigger.",
    ]}
  />
);
