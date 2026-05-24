import { http } from "@/lib/http-client";
import { AI_ENDPOINTS } from "@blocks-workflow/constants/ai.endpoint.constant";
import {
  IGetAgentsPayload,
  IGetAgentsResponse,
} from "../types/agent.service.type";
import { API_BASES } from "@/constants/endpoint.constant";
import { getRuntimeEnv } from "@/lib/runtime-env";

export class AgentService {
  getAgents(payload: IGetAgentsPayload): Promise<IGetAgentsResponse> {
    const baseUrl = getRuntimeEnv("BLOCKS_AGENT_API_BASE_URL") || getRuntimeEnv("BLOCKS_API_BASE_URL");
    const url = `${baseUrl}${API_BASES.AI}${AI_ENDPOINTS.AGENT_QUERIES} : `
    return http.post(
      url,
      payload,
      undefined,
      { absoluteUrl: true },
    );
  }
}

export const agentService = new AgentService();
