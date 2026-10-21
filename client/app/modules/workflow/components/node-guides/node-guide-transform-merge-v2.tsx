import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformMergeV2 = () => (
  <GuideContent
    title="Merge"
    description="Use this node to combine the items of several branches into one list. Append outputs the items of Input 1, then Input 2, and so on."
    steps={[
      "Set Number of Inputs, then connect one branch to each input. The change applies when you leave the field or press Enter.",
      "Connect the next node to the output.",
      "On the Settings tab, choose whether the node runs when only some inputs sent data, or only when all of them did.",
    ]}
    notes={[
      "Merge waits until every branch that can still run has finished.",
      "An input that receives nothing (for example an untaken If branch) adds no items.",
      "With Run When set to All inputs have data, a missing input either skips the node (no output) or fails it.",
      "Lowering Number of Inputs removes the connections on the inputs that are removed.",
      "Each item keeps the item it came from as its parent, so $node[\"…\"] expressions in later nodes still refer to its own branch.",
    ]}
  />
);
