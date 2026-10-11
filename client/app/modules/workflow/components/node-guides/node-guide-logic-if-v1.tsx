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
      "Equals one of (list) checks that the value exactly equals one entry of the list (case-sensitive). All values are in (list) checks that every value of the left list is in the right list. Write the list as a JSON array or comma-separated values. An empty list makes these false, and their \"not\" versions true.",
      "An item whose condition cannot be evaluated (for example, an unknown operator or one that does not fit the type) fails the step.",
      "Empty or incomplete conditions may not route the workflow the way you expect, so test both passing and failing cases.",
    ]}
  />
);
