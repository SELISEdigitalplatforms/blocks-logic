import { serviceInstances } from "@/lib/http-client";
import { AI_ENDPOINTS } from "@/modules/workflow/constants/agents.endpoint.constant";
import {
  IGetAgentsPayload,
  IGetAgentsResponse,
} from "../types/agent.service.type";

export class AgentService {
  private readonly AgentHttpClient = serviceInstances.agentsService;
  getAgents(payload: IGetAgentsPayload): Promise<IGetAgentsResponse> {
    return this.AgentHttpClient.post(
      AI_ENDPOINTS.AGENT_QUERIES,
      payload,
      undefined,
      { absoluteUrl: true },
    );
  }
}

export const agentService = new AgentService();
