import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerErrorTriggerV2 = () => (
  <GuideContent
    title="Error Trigger"
    description="Use this trigger to start this workflow when a production run of another workflow fails."
    steps={[
      "Add the Error Trigger and the nodes that should react, for example Send Mail.",
      "Publish this workflow.",
      "Open the other workflow's ⋮ menu, choose Settings, turn on Alert on failure and select this workflow.",
    ]}
    notes={[
      "The output has the failed run in {{ $json.execution }} (id, attemptNumber, mode, startedAt, finishedAt and error) and the failed workflow in {{ $json.workflow }} (id and name).",
      "Test step uses sample data, because test runs never send alerts.",
      "A workflow can have one Error Trigger.",
    ]}
  />
);
