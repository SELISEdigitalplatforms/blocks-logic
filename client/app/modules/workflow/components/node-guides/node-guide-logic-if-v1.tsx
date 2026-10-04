import { GuideContent } from "./node-guide-content";

export const NodeGuideLogicIfV1 = () => (
  <GuideContent
    title="If logic"
    description="Use this node to choose a workflow path based on conditions. Each input item is evaluated separately and then sent unchanged to either the true or false branch."
    steps={[
      "Use All conditions (AND) when every condition must be true.",
      "Use Any condition (OR) when one matching condition should be enough, then test both branches with sample data.",
      "Add the conditions that should be evaluated against data from earlier nodes.",
      "Connect the following nodes to the appropriate branch for the result you expect.",
    ]}
    notes={[
      "Condition values are resolved as expressions before comparison.",
      "Number, boolean, date/time, and array comparisons are parsed by the selected condition type.",
      "The node starts with All conditions (AND) and no conditions.",
      "Empty or incomplete conditions may not route the workflow the way you expect, so test both passing and failing cases.",
    ]}
  />
);
