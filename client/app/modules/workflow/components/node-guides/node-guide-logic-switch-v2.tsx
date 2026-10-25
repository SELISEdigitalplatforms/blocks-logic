import { GuideContent } from "./node-guide-content";

export const NodeGuideLogicSwitchV2 = () => (
  <GuideContent
    title="Switch"
    description="Use this node to send each item to one of many paths. Every output has its own conditions."
    steps={[
      "Add one routing rule per output. Rules are checked from top to bottom and the item goes to the first one that matches.",
      "In each rule, pick the type and operator, then the value to compare with. Use AND or OR to choose whether all conditions or any one must be true.",
      "Turn on Rename Output to give an output a name. Without a name it shows its number: 0, 1, 2.",
      "Choose what happens to items that match no rule: drop them, send them to an extra Fallback output, or send them to one of the rules' outputs.",
      "Connect the following nodes to the outputs.",
    ]}
    notes={[
      "Conditions work exactly like the If node, with the same types and operators. Text comparisons are case-sensitive.",
      "A value that is not a valid number, date or true/false makes its condition not match.",
      "Renaming or deleting an output never moves the connections of the other outputs. Deleting an output removes its connections.",
      "An item whose conditions cannot be evaluated fails the step.",
    ]}
  />
);
