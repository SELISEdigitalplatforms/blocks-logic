import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerDataGatewayV1 = () => (
  <GuideContent
    title="Data Gateway trigger"
    description="Use this trigger to start a workflow when records in a Data Gateway collection are inserted, updated, or deleted."
    steps={[
      "Select the Collection to monitor.",
      "Choose the Operation that should start the workflow: Inserted, Updated, or Deleted.",
      "Review the output preview so later nodes can use the fields this trigger provides.",
      "In editor test mode, use data marked for test execution as described in the node notes.",
    ]}
    notes={[
      "The project key is captured from the selected project.",
      "Insert and delete payloads include operation details, document id when available, timestamp, and document fields.",
      "Update payloads include UpdatedFields with field name, old value, and new value instead of the full document fields.",
      "Records whose Tags include mock-data run matching workflows in test mode. Normal records use published workflows.",
    ]}
  />
);
