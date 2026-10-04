import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformSetFieldV1 = () => (
  <GuideContent
    title="Set Field transform"
    description="Use this node to shape the output item by setting new field values manually or by providing JSON. It can also carry selected fields from the input item into the output."
    steps={[
      "Choose Manual Mapping to add fields one by one, or JSON to enter an output object directly.",
      "In Manual Mapping, choose the value type for each field so numbers and booleans are written as those types.",
      "In JSON mode, enter a valid JSON object. Expressions inside the JSON are resolved for each input item.",
      "Turn on Include Other Input Fields if the output should keep data from the incoming item.",
      "When including input fields, choose All Fields, Specific Fields, or Exclude Fields.",
    ]}
    notes={[
      "The node runs once for each input item.",
      "Fields you set are merged on top of the included input fields, so a new value can replace an included field with the same name.",
      "By default, other input fields are not included.",
      "Specific include or exclude lists are comma-separated, so keep field names aligned with the incoming data.",
    ]}
  />
);
