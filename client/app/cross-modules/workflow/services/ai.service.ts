import { AgentService } from "./agent.service";

export class AIService {
  constructor(public agent: AgentService) {}
}

export const aiService = new AIService(new AgentService());
