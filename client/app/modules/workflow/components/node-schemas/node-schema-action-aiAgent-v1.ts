import { API_BASES } from "@/constants/endpoint.constant";
import { agentService } from "@/modules/workflow/services/agent.service";
import { NodeGuideActionAiAgentV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";
import { tStatic } from "@seliseblocks/genesis-os";

export const NodeSchemaActionAiAgentV1: NodeSchemaDefinition = {
  guide: NodeGuideActionAiAgentV1,
  schema: {
    type: "agent",
    category: "action",
    version: "v1",
    parameters: [
      {
        id: "agent",
        type: "select",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_AI_AGENT_V1.AGENT");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_AI_AGENT_V1.THE_AI_AGENT_THIS",
          );
        },
        key: "agent",
        required: true,
        options: (_data, config) => {
          return new Promise((resolve, reject) => {
            agentService
              .getAgents({
                limit: 100,
                offset: 0,
              })
              .then((res) =>
                resolve(
                  res.agents.map((agent) => ({
                    value: `${agent.id}_${agent.widget_id}_${config.tenantId}`,
                    label: agent.name,
                  })),
                ),
              )
              .catch(reject);
          });
        },
        onChange: (value: unknown) => {
          const [AgentId, WidgetId] = (value as string).split("_");
          return {
            agent: value,
            AgentId,
            WidgetId,
          };
        },
      },
      {
        id: "path",
        type: "textarea",
        get label() {
          return tStatic("logic.workflow.nodes.schemas", "NODE_SCHEMA_ACTION_AI_AGENT_V1.INPUT");
        },
        get info() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_AI_AGENT_V1.ENTER_YOUR_MESSAGE_TO",
          );
        },
        key: "input",
        required: true,
        get placeholder() {
          return tStatic(
            "logic.workflow.nodes.schemas",
            "NODE_SCHEMA_ACTION_AI_AGENT_V1.ENTER_YOUR_MESSAGE_TO",
          );
        },
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      AgentId: "",
      WidgetId: "",
      input: "",
      ApiBaseUrl: "",
    },
    settings: {},
  },
  transform: (node) => ({
    ...node,
    parameters: {
      ...node.parameters,
      ApiBaseUrl: API_BASES.AGENTS,
    },
  }),
};
