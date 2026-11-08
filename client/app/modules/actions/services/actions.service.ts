import { serviceInstances } from "@/lib/http-client";
import { ACTIONS_ENDPOINTS } from "../constants/endpoint.constant";
import { IActionOption } from "../types/actions.type";

export class ActionsService {
  private readonly logicHttpClient = serviceInstances.logicService;

  /** Actions with the Workflow switch on. */
  getForWorkflow = (): Promise<IActionOption[]> => {
    return this.logicHttpClient.get(ACTIONS_ENDPOINTS.GET_FOR_WORKFLOW);
  };

  /** Actions with the Function switch on. */
  getForFunction = (): Promise<IActionOption[]> => {
    return this.logicHttpClient.get(ACTIONS_ENDPOINTS.GET_FOR_FUNCTION);
  };
}

export const actionsService = new ActionsService();
