import { http, HttpResponse, type JsonBodyType } from "msw";
import { WORKFLOW_ENDPOINTS } from "@blocks-workflow/constants/endpoint.constant";
import {
  mockGetWorkflowsResponse,
  mockGetWorkflowByIdResponse,
  mockCreateWorkflowResponse,
  mockDuplicateWorkflowResponse,
  mockUpdateWorkflowResponse,
  mockDeleteWorkflowResponse,
  mockGetWorkflowExecutionsResponse,
  mockGetWorkflowExecutionByIdResponse,
} from "../__mocks__/data.mock";

// ─── URL Patterns ─────────────────────────────────────────────────────────────
// Note: GET is a substring of GetAll/GetExecutions/GetExecution — anchor with query param
// Note: GetExecution is a substring of GetExecutions — anchor with "?" separator
const GET_ALL_PATTERN = new RegExp(WORKFLOW_ENDPOINTS.GET_ALL);
const GET_WORKFLOW_PATTERN = new RegExp(`${WORKFLOW_ENDPOINTS.GET}\\?WorkflowId=`);
const CREATE_PATTERN = new RegExp(WORKFLOW_ENDPOINTS.CREATE);
const DUPLICATE_PATTERN = new RegExp(WORKFLOW_ENDPOINTS.DUPLICATE);
const UPDATE_PATTERN = new RegExp(WORKFLOW_ENDPOINTS.UPDATE);
const DELETE_PATTERN = new RegExp(`${WORKFLOW_ENDPOINTS.DELETE}\\?id=`);
const GET_EXECUTIONS_PATTERN = new RegExp(WORKFLOW_ENDPOINTS.GET_EXECUTIONS);
const GET_EXECUTION_PATTERN = new RegExp(`${WORKFLOW_ENDPOINTS.GET_EXECUTION}\\?`);

// ─── Default Handlers (happy path) ───────────────────────────────────────────
export const workflowHandlers = [
  http.post(GET_ALL_PATTERN, () => HttpResponse.json(mockGetWorkflowsResponse)),
  http.get(GET_WORKFLOW_PATTERN, () => HttpResponse.json(mockGetWorkflowByIdResponse)),
  http.post(CREATE_PATTERN, () => HttpResponse.json(mockCreateWorkflowResponse)),
  http.post(DUPLICATE_PATTERN, () => HttpResponse.json(mockDuplicateWorkflowResponse)),
  http.put(UPDATE_PATTERN, () => HttpResponse.json(mockUpdateWorkflowResponse)),
  http.delete(DELETE_PATTERN, () => HttpResponse.json(mockDeleteWorkflowResponse)),
  http.get(GET_EXECUTIONS_PATTERN, () => HttpResponse.json(mockGetWorkflowExecutionsResponse)),
  http.get(GET_EXECUTION_PATTERN, () => HttpResponse.json(mockGetWorkflowExecutionByIdResponse)),
];

// ─── Per-test Handler Factories ───────────────────────────────────────────────
export const getWorkflowsHandler = (response: JsonBodyType) =>
  http.post(GET_ALL_PATTERN, () => HttpResponse.json(response));

export const getWorkflowByIdHandler = (response: JsonBodyType) =>
  http.get(GET_WORKFLOW_PATTERN, () => HttpResponse.json(response));

export const createWorkflowHandler = (response: JsonBodyType) =>
  http.post(CREATE_PATTERN, () => HttpResponse.json(response));

export const duplicateWorkflowHandler = (response: JsonBodyType) =>
  http.post(DUPLICATE_PATTERN, () => HttpResponse.json(response));

export const updateWorkflowHandler = (response: JsonBodyType) =>
  http.put(UPDATE_PATTERN, () => HttpResponse.json(response));

export const deleteWorkflowHandler = (response: JsonBodyType) =>
  http.delete(DELETE_PATTERN, () => HttpResponse.json(response));

export const getWorkflowExecutionsHandler = (response: JsonBodyType) =>
  http.get(GET_EXECUTIONS_PATTERN, () => HttpResponse.json(response));

export const getWorkflowExecutionByIdHandler = (response: JsonBodyType) =>
  http.get(GET_EXECUTION_PATTERN, () => HttpResponse.json(response));

// ─── Error Handler Factories ──────────────────────────────────────────────────
export const getWorkflowsErrorHandler = (status = 500) =>
  http.post(GET_ALL_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getWorkflowByIdErrorHandler = (status = 500) =>
  http.get(GET_WORKFLOW_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const createWorkflowErrorHandler = (status = 500) =>
  http.post(CREATE_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );

export const getWorkflowExecutionsErrorHandler = (status = 500) =>
  http.get(GET_EXECUTIONS_PATTERN, () =>
    HttpResponse.json({ message: "Internal server error" }, { status }),
  );
