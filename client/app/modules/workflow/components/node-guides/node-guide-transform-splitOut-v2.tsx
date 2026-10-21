import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformSplitOutV2 = () => (
  <GuideContent
    title="Split Out"
    description="Use this node to turn a list inside each item into separate items, so that the next nodes handle each element on its own."
    steps={[
      "In Fields To Split Out, drag the field that holds the list from the input panel, or type its name, for example lines or order.lines.",
      "Optionally, enter an Output Field Name to store each element under a new name. A name with dots, such as order.line, is written inside order.",
      "Choose in Include which other fields of the input item each new item keeps: none, all, or only the fields you list.",
      "Add another field to split several lists together.",
    ]}
    notes={[
      "Several fields are combined by position: the first new item gets the first element of each field, and so on. A field that is not a list reaches only the first new item.",
      "An object is split by its values. A single value (including null) gives one item.",
      "With No other fields, one field and no output field name, an object element becomes the new item itself.",
      "An item without the field gives no items. After a run, the output shows which fields were not found in any input item.",
      "Turn on Disable Dot Notation to read a key that contains a dot.",
      "Each new item keeps its original item as its parent, so $node[\"…\"] expressions in later nodes still refer to the original item.",
    ]}
  />
);
