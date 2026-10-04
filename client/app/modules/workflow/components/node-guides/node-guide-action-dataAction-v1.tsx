import { GuideContent } from "./node-guide-content";

export const NodeGuideActionDataActionV1 = () => (
  <GuideContent
    title="Data Action action"
    description="Use this node to read or change records through Data Gateway. New configurations should use Raw Query mode, while the guided collection fields remain available for existing flows."
    steps={[
      "Keep Raw Query Mode on for a direct GraphQL request, then enter the query or mutation in Raw Query.",
      "If authentication is needed, choose Client Credential and select an active credential, or Blocks Authentication to reuse the run's delegated token.",
      "For guided mode, turn Raw Query Mode off, select a Collection, then choose Get, Insert, Update, or Delete.",
      "For Get, Update, or Delete, add filters that narrow the records. For Insert or Update, fill the field mapping values.",
      "For Get, review the selected Fields and remove fields you do not need.",
    ]}
    notes={[
      "Raw Query runs once for each input item, and expressions in the query are resolved before it is sent.",
      "Guided Get returns one output item per returned record when the response contains an items array.",
      "Guided Insert, Update, and Delete return an action result with status and item information.",
      "Guided field mappings are converted using the selected collection schema where possible.",
      "The schema currently warns that guided query fields are being phased out, so prefer Raw Query for new nodes.",
      "Selecting a collection can auto-populate schema fields and reset filters, field mappings, and selected get fields.",
    ]}
  />
);
