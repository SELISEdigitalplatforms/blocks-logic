import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformCodeV1 = () => (
  <GuideContent
    title="Code transform"
    description="Use this node to reshape, filter, calculate, or format workflow data with JavaScript. Return the data that should continue to the next node."
    steps={[
      "Choose Run Once for All Items when the script needs the full input list, or Run Once for Each Item when the same logic should run for every item.",
      "Leave Language set to JavaScript.",
      "In all-items mode, read from $items and return one object or an array of objects, for example: return $items.map(item => ({ name: item.json.name }));",
      "In each-item mode, read the current item from $json and return an object, for example: return { name: $json.name };",
      "Use $node to read data from earlier connected nodes when the transform depends on previous results.",
      "Test with sample data that matches the shape this node will receive in the workflow.",
    ]}
    notes={[
      "Only JavaScript is supported.",
      "If the script does not return a value, the node produces no output items.",
      "Returning an array creates multiple output items. Returning an object is usually the clearest option for downstream nodes.",
      "Code runs in a sandbox for data transformation, so external API calls, file access, package imports, and system commands are not available.",
      "Use JavaScript variables like $json, $items, and $node inside the script instead of expression syntax such as {{...}}.",
    ]}
  />
);
