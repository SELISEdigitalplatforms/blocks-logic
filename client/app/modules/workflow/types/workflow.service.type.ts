import { Edge } from "@xyflow/react";
import { ExecutedItem, ExecutedNode, Workflow, WorkflowSummary, WorkflowVersion, WorkflowExecutionMode } from "../models/workflow.model";
import { WorkflowNode } from "@blocks-workflow/models/node.model";
import { OutputSchemaField } from "./output-schema.types";

export interface IGetWorkflowsPayload {
  pageSize?: number;
  pageNumber?: number;
  projectKey: string;
  search?: string;
  isPublished?: boolean;
}

export interface IGetWorkflowsResponse {
  data: WorkflowSummary[];
  totalCount: number;
  errors: unknown;
}

export interface IGetWorkflowByIdPayload {
  id: string;
  projectKey: string;
}

export interface IGetWorkflowByIdResponse {
  data: Workflow;
  isSuccess: boolean;
  errors: unknown;
}

export interface ICreateWorkflowPayload {
  name: string;
  projectKey: string;
  description?: string;
  nodes?: WorkflowNode[];
  edges?: Edge[];
  settings?: Record<string, unknown>;
  // isActive?: boolean;
  nodeOutputSchemas?: Record<string, OutputSchemaField[]>;
}

export interface ICreateWorkflowResponse {
  itemId: string;
  isSuccess: boolean;
  errors?: unknown;
}

export interface IDuplicateWorkflowPayload {
  name: string;
  workflowId: string;
  projectKey: string;
}

export interface IDuplicateWorkflowResponse {
  itemId: string;
  isSuccess: boolean;
  errors?: unknown;
}

export interface IUpdateWorkflowPayload extends Omit<ICreateWorkflowPayload, "name"> {
  itemId: string;
  projectKey: string;
  name?: string;
}

export interface IUpdateWorkflowResponse {
  id: string;
  isSuccess: boolean;
  errors?: unknown;
}

export interface IDeleteWorkflowPayload {
  id: string;
  projectKey: string;
}

export interface IDeleteWorkflowResponse {
  itemId: string;
  isSuccess: boolean;
  errors: unknown;
}

export interface IGetWorkflowExecutionsPayload {
  projectKey: string;
  workflowId: string;
}

export interface WorkflowExecution {
  id: string;
  workflowId: string;
  projectKey: string;
  status: number;
  executionMode: WorkflowExecutionMode;
  startedAt: string;
  finishedAt: string;
  duration: number;
  triggeredBy: string;
  errorMessage: string;
}

export interface IGetWorkflowExecutionsResponse {
  data: WorkflowExecution[];
  totalCount: number;
  errors: unknown;
}

export interface IGetWorkflowExecutionByIdPayload {
  projectKey: string;
  executionId: string;
}

export interface IGetWorkflowExecutionByIdResponse {
  data: IGetWorkflowExecutionById;
  isSuccess: boolean;
  errors: unknown;
}

export interface IGetWorkflowExecutionById {
  workflowSnapshot: Workflow;
  nodeExecutions: ExecutedNode[];
  items: ExecutedItem[];
  executionMode: WorkflowExecutionMode;
  id: string;
}

export interface ICreateWorkflowVersionPayload {
  projectKey: string;
  workflowId: string;
  name: string;
  Description?: string;
}

export interface ICreateWorkflowVersionResponse {
  [key: string]: any;
}

export interface IGetWorkflowVersionsPayload {
  projectKey: string;
  workflowId: string;
}

export interface IGetWorkflowVersionsResponse {
  data: WorkflowVersion[];
  totalCount: number;
  errors: unknown;
}

export interface IPublishWorkflowPayload {
  workflowId: string;
  projectKey: string;
  versionId?: string;
}

export interface IPublishWorkflowResponse {
  itemId: string;
  isSuccess: boolean;
  errors: unknown;
}

export interface IPublishNewWorkflowPayload {
  workflowId: string;
  projectKey: string;
  name?: string;
  description?: string;
}

export interface IUnpublishWorkflowPayload {
  workflowId: string;
  projectKey: string;
}

export interface IUnpublishWorkflowResponse {
  itemId: string;
  isSuccess: boolean;
  errors: unknown;
}

export interface IRestoreWorkflowPayload {
  workflowId: string;
  projectKey: string;
  versionId: string;
}

export interface IRestoreWorkflowResponse {
  [key: string]: any;
}

export interface IGetWorkflowByVersionPayload {
  workflowId: string;
  projectKey: string;
  versionId: string;
}

export interface IGetWorkflowByVersionResponse {
  [key: string]: any;
}

export interface IUpdateWorkflowVersionPayload {
  projectKey: string;
  versionId: string;
  name?: string;
  description?: string;
}

export interface IUpdateWorkflowVersionResponse {
  itemId: string;
  isSuccess: boolean;
  errors?: unknown;
}

export interface IGetLastSuccessfulExecutionPayload {
  projectKey: string;
  workflowId: string;
}

// export interface IGetLastSuccessfulExecutionResponse {
//   data: IGetLastSuccessfulExecution;
//   isSuccess: boolean;
//   errors: unknown;
// }

// export interface IGetLastSuccessfulExecution {
//   id: string;
//   workflowId: string;
//   workflowName: string;
//   status: number;
//   executionMode: WorkflowExecutionMode;
//   startedAt: string;
//   finishedAt: string;
//   duration: number | null;
//   errorMessage: string | null;
//   triggerType: string;
//   attemptNumber: number;
//   triggerMetadata: Record<string, any>;
//   context: Record<string, any>;
//   activeNodeIds: string[];
//   nodeExecutions: ExecutedNode[];
//   workflowSnapshot: Workflow;
//   items: ExecutedItem[];
// }

export interface IStepExecutePayload {
  ProjectKey: string;
  WorkflowId: string;
  NodeId: string;
  SourceExecutionId?: string;
}

export interface IStepExecuteResponse {
  itemId: string;
  isSuccess: boolean;
  errors?: unknown;
  message: string;
  code: string;
}

export interface ITriggerListenerPayload {
  ProjectKey: string;
  WorkflowId: string;
  TriggerId: string;
  EnableListener: boolean;
  CompletionNodeId?: string;
}

export interface ITriggerListenerResponse {
  itemId: string;
  errors: unknown;
  isSuccess: boolean;
}
