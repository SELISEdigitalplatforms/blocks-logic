import { GuideContent } from "./node-guide-content";

export const NodeGuideTransformMergeV2 = () => (
  <GuideContent
    title="Merge"
    description="Use this node to combine the items of several branches into one list. Append outputs the items of Input 1, then Input 2, and so on."
    steps={[
      "Set Number of Inputs (2 to 10), then connect one branch to each input.",
      "Choose Run When: All outputs only when every connected input has data; Any outputs whatever arrived.",
      "Connect the next node to the output.",
    ]}
    notes={[
      "Merge waits until every branch that can still run has finished, then runs once.",
      "An input that receives nothing (an untaken If branch, or a step that output no items) has no data. With All, Merge then outputs nothing; with Any, that input adds no items.",
      "If no input receives any items, Merge outputs nothing and the next node does not run.",
      "Lowering Number of Inputs removes the connections on the inputs that are removed.",
      "Each item keeps the item it came from as its parent, so $node[\"…\"] expressions in later nodes still refer to its own branch.",
    ]}
  />
);
