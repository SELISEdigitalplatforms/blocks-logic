import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformCodeV1 = () => (
  <GuideContent
    title="Code transform"
    description="Use this node to transform workflow data with JavaScript. The script must return the data that should continue to the next node."
    steps={[
      "Choose Run Once for All Items when the script should receive the full input list, or Run Once for Each Item when it should run separately for every input item.",
      "Leave Language set to JavaScript.",
      "In all-items mode, read input from $items and return one object or an array of objects, for example: return $items.map(item => ({ name: item.json.name }));",
      "In each-item mode, read the current item from $json or $item and return an object, for example: return { name: $json.name };",
      "Test with data that matches the shape this node will receive in the workflow.",
    ]}
    notes={[
      "Python is listed but disabled in the schema.",
      "If the script does not return a value, the node produces no output items.",
      "Returning an array creates multiple output items. Returning a primitive value is wrapped under a json field, so returning an object is usually the clearest option.",
      "Access to network, filesystem, process, and require-style globals is blocked by the backend sandbox.",
    ]}
  />
);
