import { Edge } from "@xyflow/react";
import { WorkflowNode } from "./node.model";
import { OutputSchemaField } from "@blocks-workflow/types/output-schema.types";

export type ExecutedItem = {
  itemId: string;
  nodeId: string;
  nodeExecutionId: string;
  branch: string;
  data: {
    Parameters: Record<string, unknown>;
    Input: Record<string, unknown>;
    Output: Record<string, unknown>;
  };
  parentItemIds: string[] | null;
  itemIndex: number;
  createdAt: string;
};

export interface WorkflowSummary {
  createdBy: string;
  createdDate: string;
  isActive: boolean;
  itemId: string;
  language: string | null;
  lastUpdatedBy: string;
  lastUpdatedDate: string;
  name: string;
  tags: [];
}

export interface WorkflowEdge extends Edge {
  id: string;
  source: string;
  target: string;
  sourceHandle: string;
  targetHandle: string;
}

export interface Workflow extends WorkflowSummary {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  settings: Record<string, unknown>;
  nodeOutputSchemas?: Record<string, OutputSchemaField[]>;
  items?: ExecutedItem[];
  nodeExecutions?: ExecutedNode[];
}

export interface WorkflowStep {
  id: string;
  name: string;
  type: string;
  order: number;
  configuration: Record<string, unknown>;
}

export interface ExecutedNode {
  parameters: Record<string, unknown>;
  input: Record<string, unknown>[];
  output: Record<string, unknown>[];
  id: string;
  nodeId: string;
  nodeName: string;
  nodeType: "webhook";
  nodeVersion: "v1";
  runIndex: number;
  status: number;
  inputItemCount: number;
  outputItemCount: number;
  outputCountsByBranch: Record<string, number>;
  startedAt: string;
  endedAt: string;
  error: string | null;
  attemptNumber: number;
}

export interface WorkflowVersion {
  id: string;
  name: string;
  description?: string;
  author?: string;
  date?: string;
  isActive?: boolean;
  [key: string]: any;
}
