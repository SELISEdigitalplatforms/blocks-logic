import { http } from "@/lib/http-client";
import { WORKFLOW_ENDPOINTS } from "../constants/endpoint.constant";
import {
  IGetWorkflowsPayload,
  IGetWorkflowsResponse,
  IGetWorkflowByIdPayload,
  IGetWorkflowByIdResponse,
  ICreateWorkflowPayload,
  ICreateWorkflowResponse,
  IUpdateWorkflowPayload,
  IUpdateWorkflowResponse,
  IDeleteWorkflowPayload,
  IDeleteWorkflowResponse,
  IDuplicateWorkflowPayload,
  IDuplicateWorkflowResponse,
  IGetWorkflowExecutionsPayload,
  IGetWorkflowExecutionsResponse,
  IGetWorkflowExecutionByIdPayload,
  IGetWorkflowExecutionByIdResponse,
} from "../types/workflow.service.type";

export class WorkflowService {
  getWorkflows(payload: IGetWorkflowsPayload): Promise<IGetWorkflowsResponse> {
    return http.post(`${WORKFLOW_ENDPOINTS.GET_ALL}`, payload);
    // return http.post("http://localhost:5054/Workflow/GetAll", payload, {}, { absoluteUrl: true });
  }

  async getWorkflowById(payload: IGetWorkflowByIdPayload): Promise<IGetWorkflowByIdResponse> {
    const response = await http.get<IGetWorkflowByIdResponse>(
      `${WORKFLOW_ENDPOINTS.GET}?WorkflowId=${payload.id}&projectKey=${payload.projectKey}`,
    );

    // const response = await http.get<IGetWorkflowByIdResponse>(
    //   `http://localhost:5054/Workflow/Get?WorkflowId=${payload.id}&projectKey=${payload.projectKey}`,
    //   {},
    //   { absoluteUrl: true },
    // );

    return response;
  }

  createWorkflow(payload: ICreateWorkflowPayload): Promise<ICreateWorkflowResponse> {
    return http.post(`${WORKFLOW_ENDPOINTS.CREATE}`, payload);
    // return http.post(`http://localhost:5054/Workflow/Create`, payload, {}, { absoluteUrl: true });
  }

  duplicateWorkflow(payload: IDuplicateWorkflowPayload): Promise<IDuplicateWorkflowResponse> {
    return http.post(`${WORKFLOW_ENDPOINTS.DUPLICATE}`, payload);
    // return http.post(
    //   `http://localhost:5054/Workflow/Duplicate`,
    //   payload,
    //   {},
    //   { absoluteUrl: true },
    // );
  }

  updateWorkflow(payload: IUpdateWorkflowPayload): Promise<IUpdateWorkflowResponse> {
    return http.put(`${WORKFLOW_ENDPOINTS.UPDATE}`, payload);
    // return http.put(`http://localhost:5054/Workflow/Update`, payload, {}, { absoluteUrl: true });
  }

  deleteWorkflow(payload: IDeleteWorkflowPayload): Promise<IDeleteWorkflowResponse> {
    return http.delete(
      `${WORKFLOW_ENDPOINTS.DELETE}?id=${payload.id}&projectKey=${payload.projectKey}`,
    );
    // return http.delete(
    //   `http://localhost:5054/Workflow/Delete?id=${payload.id}&projectKey=${payload.projectKey}`,
    //   {},
    //   { absoluteUrl: true },
    // );
  }

  getWorkflowExecutions(
    payload: IGetWorkflowExecutionsPayload,
  ): Promise<IGetWorkflowExecutionsResponse> {
    const params = new URLSearchParams({
      ProjectKey: payload.projectKey,
      WorkflowId: payload.workflowId,
    });
    return http.get(`${WORKFLOW_ENDPOINTS.GET_EXECUTIONS}?${params.toString()}`);
    // return http.get(
    //   `http://localhost:5054/Workflow/GetExecutions?${params.toString()}`,
    //   {},
    //   { absoluteUrl: true },
    // );
  }

  getWorkflowExecutionById(
    payload: IGetWorkflowExecutionByIdPayload,
  ): Promise<IGetWorkflowExecutionByIdResponse> {
    const params = new URLSearchParams({
      ProjectKey: payload.projectKey,
      ExecutionId: payload.executionId,
    });
    return http.get(`${WORKFLOW_ENDPOINTS.GET_EXECUTION}?${params.toString()}`);
    // return http.get(
    //   `http://localhost:5054/Workflow/GetExecution?${params.toString()}`,
    //   {},
    //   { absoluteUrl: true },
    // );
  }
}

export const workflowService = new WorkflowService();
