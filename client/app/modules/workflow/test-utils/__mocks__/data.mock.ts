import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import type {
  ExecutedItem,
  Workflow,
  WorkflowEdge,
  WorkflowSummary,
} from "@blocks-workflow/models/workflow.model";
import type { WorkflowNode } from "@blocks-workflow/models/node.model";
import type {
  ICreateWorkflowResponse,
  IDeleteWorkflowResponse,
  IDuplicateWorkflowResponse,
  IGetWorkflowByIdResponse,
  IGetWorkflowExecutionByIdResponse,
  IGetWorkflowExecutionsResponse,
  IGetWorkflowsResponse,
  IUpdateWorkflowResponse,
  WorkflowExecution,
} from "@blocks-workflow/types/workflow.service.type";

// ─── Mock IDs ─────────────────────────────────────────────────────────────────
export const MOCK_WORKFLOW_ID_1 = "a1b2-c3d4-e5f6-0001";
export const MOCK_WORKFLOW_ID_2 = "a1b2-c3d4-e5f6-0002";
export const MOCK_WORKFLOW_NODE_ID_1 = "b2c3-d4e5-f6a7-0001";
export const MOCK_WORKFLOW_NODE_ID_2 = "b2c3-d4e5-f6a7-0002";
export const MOCK_WORKFLOW_EDGE_ID_1 = "c3d4-e5f6-a7b8-0001";
export const MOCK_WORKFLOW_EXECUTION_ID_1 = "d4e5-f6a7-b8c9-0001";
export const MOCK_USER_ID = "e5f6-a7b8-c9d0-0001";

// ─── WorkflowSummary Fixtures ─────────────────────────────────────────────────
export const mockWorkflowSummary1: WorkflowSummary = {
  itemId: MOCK_WORKFLOW_ID_1,
  name: "Test Workflow 1",
  isActive: true,
  language: null,
  tags: [],
  createdBy: MOCK_USER_ID,
  createdDate: "2026-01-15T10:00:00Z",
  lastUpdatedBy: MOCK_USER_ID,
  lastUpdatedDate: "2026-02-01T12:00:00Z",
};

export const mockWorkflowSummary2: WorkflowSummary = {
  itemId: MOCK_WORKFLOW_ID_2,
  name: "Test Workflow 2",
  isActive: false,
  language: null,
  tags: [],
  createdBy: MOCK_USER_ID,
  createdDate: "2026-01-20T09:00:00Z",
  lastUpdatedBy: MOCK_USER_ID,
  lastUpdatedDate: "2026-01-25T11:00:00Z",
};

// ─── WorkflowNode Fixture ─────────────────────────────────────────────────────
export const mockWorkflowNode1: WorkflowNode = {
  id: MOCK_WORKFLOW_NODE_ID_1,
  name: "Webhook Trigger",
  description: "Triggers the workflow via a webhook",
  type: "webhook",
  category: "trigger",
  version: "v1",
  position: { x: 100, y: 100 },
  data: {},
  parameters: { method: "POST" },
  settings: {},
};

export const mockWorkflowNode2: WorkflowNode = {
  id: MOCK_WORKFLOW_NODE_ID_2,
  name: "Send Email Action",
  description: "Sends an email notification",
  type: "sendMail",
  category: "action",
  version: "v1",
  position: { x: 400, y: 100 },
  data: {},
  parameters: { to: "test@example.com", subject: "Hello" },
  settings: {},
};

// ─── WorkflowEdge Fixture ─────────────────────────────────────────────────────
export const mockWorkflowEdge1: WorkflowEdge = {
  id: MOCK_WORKFLOW_EDGE_ID_1,
  source: MOCK_WORKFLOW_NODE_ID_1,
  target: MOCK_WORKFLOW_NODE_ID_2,
  sourceHandle: "output",
  targetHandle: "input",
};

// ─── Full Workflow Fixture ────────────────────────────────────────────────────
export const mockWorkflow1: Workflow = {
  ...mockWorkflowSummary1,
  nodes: [mockWorkflowNode1, mockWorkflowNode2],
  edges: [mockWorkflowEdge1],
  settings: {},
};

// ─── WorkflowExecution Fixture ────────────────────────────────────────────────
export const mockWorkflowExecution1: WorkflowExecution = {
  id: MOCK_WORKFLOW_EXECUTION_ID_1,
  workflowId: MOCK_WORKFLOW_ID_1,
  projectKey: TEST_PROJECT_KEY,
  status: 2,
  startedAt: "2026-02-10T08:00:00Z",
  finishedAt: "2026-02-10T08:00:05Z",
  duration: 5000,
  triggeredBy: "webhook",
  errorMessage: "",
};

export const mockWorkflowExecution2: WorkflowExecution = {
  id: "d4e5-f6a7-b8c9-0002",
  workflowId: MOCK_WORKFLOW_ID_1,
  projectKey: TEST_PROJECT_KEY,
  status: 3,
  startedAt: "2026-02-11T09:00:00Z",
  finishedAt: "2026-02-11T09:00:10Z",
  duration: 10000,
  triggeredBy: "manual",
  errorMessage: "Node execution failed",
};

// ---- Execution item fixture ---------------------------------------
export const mockExecutedItem1: ExecutedItem = {
  itemId: "item-1",
  nodeId: MOCK_WORKFLOW_NODE_ID_2,
  nodeExecutionId: "node-exec-1",
  branch: "main",
  data: {
    Parameters: { method: "POST" },
    Input: { name: "John" },
    Output: { success: true },
  },
  parentItemIds: null,
  itemIndex: 0,
  createdAt: "2026-02-10T08:00:03Z",
};

export const mockExecutedItem2: ExecutedItem = {
  itemId: "item-2",
  nodeId: MOCK_WORKFLOW_NODE_ID_2,
  nodeExecutionId: "node-exec-2",
  branch: "main",
  data: {
    Parameters: { method: "POST" },
    Input: { name: "Jane" },
    Output: { success: false, error: "Email failed to send" },
  },
  parentItemIds: null,
  itemIndex: 0,
  createdAt: "2026-02-11T09:00:05Z",
};

// ─── Response Fixtures ────────────────────────────────────────────────────────
export const mockGetWorkflowsResponse: IGetWorkflowsResponse = {
  data: [mockWorkflowSummary1, mockWorkflowSummary2],
  totalCount: 2,
  errors: null,
};

export const mockGetWorkflowByIdResponse: IGetWorkflowByIdResponse = {
  data: mockWorkflow1,
  isSuccess: true,
  errors: null,
};

export const mockCreateWorkflowResponse: ICreateWorkflowResponse = {
  itemId: MOCK_WORKFLOW_ID_1,
  isSuccess: true,
  errors: null,
};

export const mockDuplicateWorkflowResponse: IDuplicateWorkflowResponse = {
  itemId: MOCK_WORKFLOW_ID_2,
  isSuccess: true,
  errors: null,
};

export const mockUpdateWorkflowResponse: IUpdateWorkflowResponse = {
  id: MOCK_WORKFLOW_ID_1,
  isSuccess: true,
  errors: null,
};

export const mockDeleteWorkflowResponse: IDeleteWorkflowResponse = {
  itemId: MOCK_WORKFLOW_ID_1,
  isSuccess: true,
  errors: null,
};

export const mockGetWorkflowExecutionsResponse: IGetWorkflowExecutionsResponse = {
  data: [mockWorkflowExecution1, mockWorkflowExecution2],
  totalCount: 2,
  errors: null,
};

export const mockGetWorkflowExecutionByIdResponse: IGetWorkflowExecutionByIdResponse = {
  workflowSnapshot: mockWorkflow1,
  nodeExecutions: [
    { isComplete: true, nodeId: MOCK_WORKFLOW_NODE_ID_1, status: 2 },
    { isComplete: false, nodeId: MOCK_WORKFLOW_NODE_ID_2, status: 3 },
  ],
  items: [mockExecutedItem1, mockExecutedItem2],
};
