import { GuideContent } from "./node-guide-content";

export const NodeGuideLogicSwitchV2 = () => (
  <GuideContent
    title="Switch"
    description="Use this node to send each item to one of many paths. Every output has its own conditions."
    steps={[
      "Add one rule per output. Rules are checked from top to bottom and the item goes to the first one that matches.",
      "Turn on Send data to all matching outputs to send a copy of the item to every rule that matches.",
      "Choose what happens to items that match no rule: drop them, send them to an extra Fallback output, or send them to one of the rules' outputs.",
      "Connect the following nodes to the outputs.",
    ]}
    notes={[
      "Conditions work exactly like the If node, with the same types and operators.",
      "Ignore case makes text comparisons (string and array) case-insensitive. Numbers, booleans and dates are not affected.",
      "Renaming, reordering or deleting an output never moves the connections of the other outputs. Deleting an output removes its connections.",
      "An item whose conditions cannot be evaluated fails the step.",
    ]}
  />
);
