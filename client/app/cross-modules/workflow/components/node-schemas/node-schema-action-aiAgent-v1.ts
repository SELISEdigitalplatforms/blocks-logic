import { aiService } from "../../services/ai.service";
import { NodeSchemaDefinition } from "./node-schema.type";

export const NodeSchemaActionAiAgentV1: NodeSchemaDefinition = {
  schema: {
    type: "agent",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "agent",
        type: "select",
        label: "Agent",
        info: "The HTTP method that will trigger this webhook",
        key: "agent",
        required: true,
        options: (_data, config) => {
          return new Promise((resolve, reject) => {
            aiService.agent
              .getAgents({
                limit: 100,
                offset: 0,
                project_key: config.projectKey,
              })
              .then((res) =>
                resolve(
                  res.agents.map((agent) => ({
                    value: `${agent.id}_${agent.widget_id}_${config.projectKey}`,
                    label: agent.name,
                  })),
                ),
              )
              .catch(reject);
          });
        },
        onChange: (value: unknown) => {
          const [AgentId, WidgetId, ProjectKey] = (value as string).split("_");
          return {
            agent: value,
            AgentId,
            WidgetId,
            ProjectKey,
          };
        },
      },
      {
        id: "path",
        type: "textarea",
        label: "Input",
        info: "Enter your message to the agent",
        key: "input",
        required: true,
        placeholder: "Enter your message to the agent",
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      AgentId: "",
      WidgetId: "",
      ProjectKey: "",
      input: "",
      ApiBaseUrl: "",
    },
    settings: {},
  },
  transform: (node) => ({
    ...node,
    parameters: {
      ...node.parameters,
      ApiBaseUrl: import.meta.env.BLOCKS_API_BASE_URL,
    },
  }),
};
