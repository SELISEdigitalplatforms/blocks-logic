import { GuideContent } from "./node-guide-content";

export const NodeGuideLogicFilterV2 = () => (
  <GuideContent
    title="Filter"
    description="Use this node to keep only the items that match the conditions. The other items are removed."
    steps={[
      "Add conditions. For each, pick the type and operator, then the value to compare with.",
      "Choose AND to keep items that match every condition, or OR to keep items that match any one.",
      "Connect the next node. It receives only the kept items, unchanged.",
    ]}
    notes={[
      "Conditions work exactly like the If and Switch nodes, with the same types and operators.",
      "Text comparisons are case-sensitive.",
      "Every condition is evaluated for every item.",
      "A value that is not a valid number, date or true/false makes its condition not match.",
      "With no conditions, AND keeps every item and OR keeps none.",
      "An item whose conditions cannot be evaluated fails the step. The matching items are still kept.",
      "The execution log shows the count, for example: Kept 1 of 3 item(s).",
      "Example: status equals paid AND total greater than 100 keeps only the paid items over 100.",
    ]}
  />
);
