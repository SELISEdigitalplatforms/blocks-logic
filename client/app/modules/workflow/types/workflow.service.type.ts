import { Edge } from "@xyflow/react";
import { ExecutedItem, ExecutedNode, Workflow, WorkflowSummary, WorkflowVersion } from "../models/workflow.model";
import { WorkflowNode } from "@blocks-workflow/models/node.model";
import { OutputSchemaField } from "./output-schema.types";

export interface IGetWorkflowsPayload {
  pageSize?: number;
  pageNumber?: number;
  projectKey: string;
  search?: string;
  isActive?: boolean;
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
  isActive?: boolean;
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
  workflowSnapshot: Workflow;
  nodeExecutions: ExecutedNode[];
  items: ExecutedItem[];
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
  data?: WorkflowVersion[];
  [key: string]: any;
}

export interface IPublishWorkflowPayload {
  workflowId: string;
  projectKey: string;
  name: string;
  Description?: string;
}

export interface IPublishWorkflowResponse {
  [key: string]: any;
}

export interface IUnpublishWorkflowPayload {
  workflowId: string;
  projectKey: string;
}

export interface IUnpublishWorkflowResponse {
  [key: string]: any;
}

export interface IRestoreWorkflowPayload {
  workflowId: string;
  projectKey: string;
  versionId: string;
}

export interface IRestoreWorkflowResponse {
  [key: string]: any;
}
