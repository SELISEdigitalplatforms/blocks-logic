import { GuideContent } from "./node-guide-content";

export const NodeGuideTriggerBlocksEventV2 = () => (
  <GuideContent
    title="Blocks Event trigger"
    description="Use this trigger to start a workflow when a Blocks service sends a platform event, such as a user change in IAM or a payment status in Utilities."
    steps={[
      "Select the Event to listen to. Only events enabled for workflows in this project are listed.",
      "To enable more events, use Manage events in the node notes; it opens the Event Triggers page in a new tab.",
      "Review the output preview so later nodes can use the event's fields.",
      "Publish the workflow: only the published workflow runs when the event happens.",
    ]}
    notes={[
      "Each event starts every published workflow that uses it, once per workflow.",
      "The output holds EventId, EventName, SourceService, ConsumerName, IsMassConsumer, Timestamp and the event's Payload.",
      "A payload that is not a JSON object is passed on as text.",
      "Publishing fails while the selected event is disabled for workflows. Disabling an event later stops its published workflows until it is enabled again.",
      "There is no editor test run for this trigger yet.",
    ]}
  />
);
