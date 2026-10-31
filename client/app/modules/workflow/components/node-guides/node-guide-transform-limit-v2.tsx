import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformLimitV2 = () => (
  <GuideContent
    title="Limit"
    description="Use this node to keep at most a set number of items, from the start or the end of the list."
    steps={[
      "Enter Max Items, the largest number of items to keep. It must be a whole number, 1 or more.",
      "Choose in Keep whether to keep the first items or the last items.",
    ]}
    notes={[
      "Limit runs once over all input items, not once per item.",
      "If there are no more items than Max Items, all of them go out.",
      "Kept items keep their input order in both modes.",
      "Each kept item keeps its original item as its parent, so $node[\"…\"] expressions in later nodes still refer to the original item.",
    ]}
  />
);
