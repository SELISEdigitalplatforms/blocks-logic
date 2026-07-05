import { serviceInstances } from "@/lib/http-client";
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
  ICreateWorkflowVersionPayload,
  ICreateWorkflowVersionResponse,
  IGetWorkflowVersionsPayload,
  IGetWorkflowVersionsResponse,
  IPublishWorkflowPayload,
  IPublishWorkflowResponse,
  IPublishNewWorkflowPayload,
  IUnpublishWorkflowPayload,
  IUnpublishWorkflowResponse,
  IRestoreWorkflowPayload,
  IRestoreWorkflowResponse,
  IGetWorkflowByVersionPayload,
  IGetWorkflowByVersionResponse,
  IUpdateWorkflowVersionPayload,
  IUpdateWorkflowVersionResponse,
  IGetLastSuccessfulExecutionPayload,
  // IGetLastSuccessfulExecutionResponse,
  IStepExecutePayload,
  IStepExecuteResponse,
} from "../types/workflow.service.type";

export class WorkflowService {
  private readonly LogicHttpClient = serviceInstances.logicService;

  getWorkflows = (payload: IGetWorkflowsPayload): Promise<IGetWorkflowsResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.GET_ALL}`, payload);
    // return this.LogicHttpClient.post("http://localhost:5054/Workflow/GetAll", payload, {}, { absoluteUrl: true });
  }

  getWorkflowById = async (payload: IGetWorkflowByIdPayload): Promise<IGetWorkflowByIdResponse> => {
    const response = await this.LogicHttpClient.get<IGetWorkflowByIdResponse>(
      `${WORKFLOW_ENDPOINTS.GET}?WorkflowId=${payload.id}&projectKey=${payload.projectKey}`,
    );

    // const response = await this.LogicHttpClient.get<IGetWorkflowByIdResponse>(
    //   `http://localhost:5054/Workflow/Get?WorkflowId=${payload.id}&projectKey=${payload.projectKey}`,
    //   {},
    //   { absoluteUrl: true },
    // );

    return response;
  }

  createWorkflow = (payload: ICreateWorkflowPayload): Promise<ICreateWorkflowResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.CREATE}`, payload);
    // return this.LogicHttpClient.post(`http://localhost:5054/Workflow/Create`, payload, {}, { absoluteUrl: true });
  }

  duplicateWorkflow = (payload: IDuplicateWorkflowPayload): Promise<IDuplicateWorkflowResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.DUPLICATE}`, payload);
    // return this.LogicHttpClient.post(
    //   `http://localhost:5054/Workflow/Duplicate`,
    //   payload,
    //   {},
    //   { absoluteUrl: true },
    // );
  }

  updateWorkflow = (payload: IUpdateWorkflowPayload): Promise<IUpdateWorkflowResponse> => {
    return this.LogicHttpClient.put(`${WORKFLOW_ENDPOINTS.UPDATE}`, payload);
    // return this.LogicHttpClient.put(`http://localhost:5054/Workflow/Update`, payload, {}, { absoluteUrl: true });
  }

  deleteWorkflow = (payload: IDeleteWorkflowPayload): Promise<IDeleteWorkflowResponse> => {
    return this.LogicHttpClient.delete(
      `${WORKFLOW_ENDPOINTS.DELETE}?id=${payload.id}&projectKey=${payload.projectKey}`,
    );
    // return this.LogicHttpClient.delete(
    //   `http://localhost:5054/Workflow/Delete?id=${payload.id}&projectKey=${payload.projectKey}`,
    //   {},
    //   { absoluteUrl: true },
    // );
  }

  getWorkflowExecutions = (
    payload: IGetWorkflowExecutionsPayload,
  ): Promise<IGetWorkflowExecutionsResponse> => {
    const params = new URLSearchParams({
      ProjectKey: payload.projectKey,
      WorkflowId: payload.workflowId,
    });
    return this.LogicHttpClient.get(`${WORKFLOW_ENDPOINTS.GET_EXECUTIONS}?${params.toString()}`);
    // return this.LogicHttpClient.get(
    //   `http://localhost:5054/Workflow/GetExecutions?${params.toString()}`,
    //   {},
    //   { absoluteUrl: true },
    // );
  }

  getWorkflowExecutionById = (
    payload: IGetWorkflowExecutionByIdPayload,
  ): Promise<IGetWorkflowExecutionByIdResponse> => {
    const params = new URLSearchParams({
      ProjectKey: payload.projectKey,
      ExecutionId: payload.executionId,
    });
    return this.LogicHttpClient.get(`${WORKFLOW_ENDPOINTS.GET_EXECUTION}?${params.toString()}`);
    // return this.LogicHttpClient.get(
    //   `http://localhost:5054/Workflow/GetExecution?${params.toString()}`,
    //   {},
    //   { absoluteUrl: true },
    // );
  }

  createWorkflowVersion = (payload: ICreateWorkflowVersionPayload): Promise<ICreateWorkflowVersionResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.CREATE_VERSION}`, payload);
  }

  getWorkflowVersions = (payload: IGetWorkflowVersionsPayload): Promise<IGetWorkflowVersionsResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.GET_VERSIONS}`, payload);
  }

  publishWorkflow = (payload: IPublishWorkflowPayload): Promise<IPublishWorkflowResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.PUBLISH}`, payload);
  }

  publishWorkflowNewVersion = (payload: IPublishNewWorkflowPayload): Promise<IPublishWorkflowResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.PUBLISH_NEW}`, payload);
  }

  unpublishWorkflow = (payload: IUnpublishWorkflowPayload): Promise<IUnpublishWorkflowResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.UNPUBLISH}`, payload);
  }

  restoreWorkflow = (payload: IRestoreWorkflowPayload): Promise<IRestoreWorkflowResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.RESTORE}`, payload);
  }

  getWorkflowByVersion = (payload: IGetWorkflowByVersionPayload): Promise<IGetWorkflowByIdResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.GET_WORKFLOW_BY_VERSION}`, payload);
  }

  updateWorkflowVersion = (payload: IUpdateWorkflowVersionPayload): Promise<IUpdateWorkflowVersionResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.UPDATE_VERSION}`, payload);
  }

  getLastSuccessfulExecution = (
    payload: IGetLastSuccessfulExecutionPayload,
  ): Promise<IGetWorkflowExecutionByIdResponse> => {
    const params = new URLSearchParams({
      projectKey: payload.projectKey,
      workflowId: payload.workflowId,
    });
    return this.LogicHttpClient.get(`${WORKFLOW_ENDPOINTS.LAST_SUCCESSFUL_EXECUTION}?${params.toString()}`);
  }

  stepExecute = (payload: IStepExecutePayload): Promise<IStepExecuteResponse> => {
    return this.LogicHttpClient.post(`${WORKFLOW_ENDPOINTS.STEP_EXECUTE}`, payload);
  }
}

export const workflowService = new WorkflowService();
