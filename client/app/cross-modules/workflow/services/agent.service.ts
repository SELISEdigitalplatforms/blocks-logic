import { http } from "@/lib/http-client";
import { AI_ENDPOINTS } from "@blocks-ai/constants/endpoint.constant";
import {
  IGetAgentsPayload,
  IGetAgentsResponse,
} from "../types/agent.service.type";
import { API_BASES } from "@/constants/endpoint.constant";

export class AgentService {
  getAgents(payload: IGetAgentsPayload): Promise<IGetAgentsResponse> {
    return http.post(
      `${API_BASES.AI}${AI_ENDPOINTS.AGENT_QUERIES}`,
      payload,
      undefined,
      { absoluteUrl: true },
    );
  }
}

export const agentService = new AgentService();
