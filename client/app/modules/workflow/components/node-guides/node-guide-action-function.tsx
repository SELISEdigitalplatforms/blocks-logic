import { GuideContent } from "./node-guide-content";

export const NodeGuideActionFunction = () => (
  <GuideContent
    title="Function action"
    description="Invokes a deployed Function synchronously and uses its returned value as this step's output."
    steps={[
      "Pick a Function. Only Live (deployed) functions are listed, and one whose workflow trigger is turned off is shown greyed out — turn that trigger on in the function\'s own Triggers settings before selecting it.",
      "Choose the Input: \"Previous step's output\" forwards the incoming item unchanged; \"Custom JSON\" opens an editor where you compose the payload, inserting values from earlier steps with {{ }} placeholders.",
      "Optionally set a Wait timeout to override how long this step waits for a result, up to 180 seconds. Leave it empty to use the function's own configured timeout plus a short grace period.",
      "Test the workflow and confirm the function's returned value maps into later steps as expected.",
    ]}
    notes={[
      "One invocation per input item — a run that does not succeed fails the whole step.",
      "If a run is still running once the wait window lapses, the step fails rather than blocking indefinitely.",
      "The function runs with the caller's identity carried through as its invocation context.",
      "A Function step with nothing wired into it still runs once when tested on its own, with no input.",
      "The function's run is recorded under the function itself, triggered by this workflow execution.",
    ]}
  />
);
