import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformSplitOutV2 = () => (
  <GuideContent
    title="Split Out"
    description="Use this node to turn a list inside each item into separate items, so that the next nodes handle each element on its own."
    steps={[
      "In Fields To Split Out, enter the field that holds the list, for example lines or order.lines. Separate several fields with commas.",
      "Choose which other fields of the item each new item keeps: none, all, or only the fields you list.",
      "Optionally, enter a Destination Field Name to store each element under a new name (one name per field).",
    ]}
    notes={[
      "Several fields are combined by position: the first new item gets the first element of each list, and so on.",
      "An object is split by its values. A single value (including null) gives one item.",
      "An item without the field gives no items. If no item has it, the next nodes do not run.",
      "Field names are plain paths, not expressions. Turn on Disable Dot Notation to read a key that contains a dot.",
      "Each new item keeps its original item as its parent, so $node[\"…\"] expressions in later nodes still refer to the original item.",
    ]}
  />
);
