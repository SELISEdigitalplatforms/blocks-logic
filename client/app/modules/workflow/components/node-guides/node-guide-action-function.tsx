import { GuideContent } from "./node-guide-content";

export const NodeGuideActionFunction = () => (
  <GuideContent
    title="Function action"
    description="Invokes a deployed Function synchronously and uses its returned value as this step's output."
    steps={[
      "Pick a Function. Only Live (deployed) functions can be selected.",
      "Choose the Input: \"Current item\" passes this step's input through unchanged; \"Expression\" lets you build the function's input from earlier steps.",
      "Optionally set a Wait timeout to override how long this step waits for a result, up to 60 seconds. Leave it empty to use the function's own configured timeout plus a short grace period.",
      "Test the workflow and confirm the function's returned value maps into later steps as expected.",
    ]}
    notes={[
      "One invocation per input item — a run that does not succeed fails the whole step.",
      "If a run is still running once the wait window lapses, the step fails rather than blocking indefinitely.",
      "The function runs with the caller's identity carried through as its invocation context.",
    ]}
  />
);
