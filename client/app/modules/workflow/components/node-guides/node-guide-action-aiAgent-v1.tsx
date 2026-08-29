import { GuideContent } from "./node-guide-content";

export const NodeGuideActionAiAgentV1 = () => (
  <GuideContent
    title="AI Agent action"
    description="Use this node to send a message into one of the project's AI agents and continue the workflow with the agent result."
    steps={[
      "Choose the Agent that should handle the request.",
      "Write the Input message the agent should receive. You can include expressions from the current item or earlier nodes.",
      "Test the node with representative input before using the result in later steps.",
    ]}
    notes={[
      "The node calls the agent once for each input item.",
      "The output is the message returned by the agent chat response.",
      "The agent list is loaded from the current project, so the selected project affects which agents are available.",
      "The node stores agent identifiers behind the scenes when you select an agent; reselect the agent if you move or recreate it.",
    ]}
  />
);
