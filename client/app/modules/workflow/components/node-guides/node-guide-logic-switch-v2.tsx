import { GuideContent } from "./node-guide-content";

export const NodeGuideLogicSwitchV2 = () => (
  <GuideContent
    title="Switch"
    description="Use this node to send each item to one of many paths. In Rules mode every output has its own conditions; in Expression mode an expression picks the output by number."
    steps={[
      "Choose Rules to route by conditions, or Expression to route by an output number.",
      "In Rules mode, add one rule per output. Rules are checked from top to bottom and the item goes to the first one that matches.",
      "Turn on Send data to all matching outputs to send a copy of the item to every rule that matches.",
      "Choose what happens to items that match no rule: drop them, send them to an extra Fallback output, or send them to one of the rules' outputs.",
      "In Expression mode, add the outputs, then write an expression that returns the 0-based output number for each item.",
      "Connect the following nodes to the outputs.",
    ]}
    notes={[
      "Conditions work exactly like the If node, with the same types and operators.",
      "Ignore case makes text comparisons (string and array) case-insensitive. Numbers, booleans and dates are not affected.",
      "Renaming, reordering or deleting an output never moves the connections of the other outputs. Deleting an output removes its connections.",
      "Each mode keeps its own outputs. Switching mode asks before removing connections on outputs the other mode does not have.",
      "An item whose conditions cannot be evaluated, or whose output number is not a whole number in range, fails the step.",
    ]}
  />
);
