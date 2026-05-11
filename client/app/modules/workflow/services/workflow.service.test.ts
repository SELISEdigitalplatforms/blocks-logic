// import { describe, it, expect, vi, beforeEach } from "vitest";
// import { mockHttpClientFactory } from "@/test-utils/__mocks__";
// import { http } from "@/lib/http-client";
// import { WORKFLOW_ENDPOINTS } from "../constants/endpoint.constant";
// import { workflowService } from "./workflow.service";
// import {
//   mockGetWorkflowsResponse,
//   mockGetWorkflowByIdResponse,
//   mockCreateWorkflowResponse,
//   mockDuplicateWorkflowResponse,
//   mockUpdateWorkflowResponse,
//   mockDeleteWorkflowResponse,
//   mockGetWorkflowExecutionsResponse,
//   mockGetWorkflowExecutionByIdResponse,
//   mockWorkflowNode1,
//   mockWorkflowNode2,
//   mockWorkflowEdge1,
//   MOCK_WORKFLOW_ID_1,
//   MOCK_WORKFLOW_EXECUTION_ID_1,
// } from "../test-utils/__mocks__";
// import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";

// vi.mock("@/lib/http-client", () => mockHttpClientFactory());

// describe.skip("WorkflowService", () => {
//   beforeEach(() => {
//     vi.clearAllMocks();
//   });

//   describe("getWorkflows", () => {
//     it("should call correct endpoint with payload", async () => {
//       vi.mocked(http.post).mockResolvedValue(mockGetWorkflowsResponse);

//       const payload = { projectKey: TEST_PROJECT_KEY, pageNumber: 1, pageSize: 10 };
//       const result = await workflowService.getWorkflows(payload);

//       expect(http.post).toHaveBeenCalledWith(WORKFLOW_ENDPOINTS.GET_ALL, payload);
//       expect(result).toEqual(mockGetWorkflowsResponse);
//     });
//   });

//   describe("getWorkflowById", () => {
//     it("should call correct endpoint and deserialize nodes", async () => {
//       const serializedResponse = {
//         ...mockGetWorkflowByIdResponse,
//         data: {
//           ...mockGetWorkflowByIdResponse.data,
//           nodes: mockGetWorkflowByIdResponse.data.nodes.map((n) => ({
//             ...n,
//             parameters: JSON.stringify(n.parameters),
//             settings: JSON.stringify(n.settings),
//           })),
//         },
//       };
//       vi.mocked(http.get).mockResolvedValue(serializedResponse);

//       const result = await workflowService.getWorkflowById({
//         id: MOCK_WORKFLOW_ID_1,
//         projectKey: TEST_PROJECT_KEY,
//       });

//       expect(http.get).toHaveBeenCalledWith(
//         `${WORKFLOW_ENDPOINTS.GET}?WorkflowId=${MOCK_WORKFLOW_ID_1}&projectKey=${TEST_PROJECT_KEY}`,
//       );
//       // nodes should be deserialized back to objects
//       expect(result.data.nodes[0].parameters).toEqual(mockWorkflowNode1.parameters);
//       expect(result.data.nodes[0].settings).toEqual(mockWorkflowNode1.settings);
//     });

//     it("should return response unchanged when nodes is undefined", async () => {
//       const responseWithoutNodes = {
//         ...mockGetWorkflowByIdResponse,
//         data: { ...mockGetWorkflowByIdResponse.data, nodes: undefined as never },
//       };
//       vi.mocked(http.get).mockResolvedValue(responseWithoutNodes);

//       const result = await workflowService.getWorkflowById({
//         id: MOCK_WORKFLOW_ID_1,
//         projectKey: TEST_PROJECT_KEY,
//       });

//       expect(result.data.nodes).toBeUndefined();
//     });
//   });

//   describe("createWorkflow", () => {
//     it("should serialize nodes before posting", async () => {
//       vi.mocked(http.post).mockResolvedValue(mockCreateWorkflowResponse);

//       await workflowService.createWorkflow({
//         name: "Test Workflow",
//         projectKey: TEST_PROJECT_KEY,
//         nodes: [mockWorkflowNode1],
//         edges: [mockWorkflowEdge1],
//       });

//       expect(http.post).toHaveBeenCalledWith(
//         WORKFLOW_ENDPOINTS.CREATE,
//         expect.objectContaining({
//           nodes: expect.arrayContaining([
//             expect.objectContaining({
//               parameters: JSON.stringify(mockWorkflowNode1.parameters),
//               settings: JSON.stringify(mockWorkflowNode1.settings),
//             }),
//           ]),
//         }),
//       );
//     });

//     it("should omit nodes key when nodes is undefined", async () => {
//       vi.mocked(http.post).mockResolvedValue(mockCreateWorkflowResponse);

//       await workflowService.createWorkflow({
//         name: "Test Workflow",
//         projectKey: TEST_PROJECT_KEY,
//       });

//       expect(http.post).toHaveBeenCalledWith(
//         WORKFLOW_ENDPOINTS.CREATE,
//         expect.objectContaining({ nodes: undefined }),
//       );
//     });
//   });

//   describe("duplicateWorkflow", () => {
//     it("should call correct endpoint with payload", async () => {
//       vi.mocked(http.post).mockResolvedValue(mockDuplicateWorkflowResponse);

//       const payload = {
//         name: "Copy of Test Workflow",
//         workflowId: MOCK_WORKFLOW_ID_1,
//         projectKey: TEST_PROJECT_KEY,
//       };
//       const result = await workflowService.duplicateWorkflow(payload);

//       expect(http.post).toHaveBeenCalledWith(WORKFLOW_ENDPOINTS.DUPLICATE, payload);
//       expect(result).toEqual(mockDuplicateWorkflowResponse);
//     });
//   });

//   describe("updateWorkflow", () => {
//     it("should serialize nodes before putting", async () => {
//       vi.mocked(http.put).mockResolvedValue(mockUpdateWorkflowResponse);

//       await workflowService.updateWorkflow({
//         itemId: MOCK_WORKFLOW_ID_1,
//         projectKey: TEST_PROJECT_KEY,
//         nodes: [mockWorkflowNode1, mockWorkflowNode2],
//         edges: [mockWorkflowEdge1],
//       });

//       expect(http.put).toHaveBeenCalledWith(
//         WORKFLOW_ENDPOINTS.UPDATE,
//         expect.objectContaining({
//           nodes: expect.arrayContaining([
//             expect.objectContaining({
//               parameters: JSON.stringify(mockWorkflowNode1.parameters),
//             }),
//           ]),
//         }),
//       );
//     });
//   });

//   describe("deleteWorkflow", () => {
//     it("should call correct endpoint with id and projectKey as query params", async () => {
//       vi.mocked(http.delete).mockResolvedValue(mockDeleteWorkflowResponse);

//       const result = await workflowService.deleteWorkflow({
//         id: MOCK_WORKFLOW_ID_1,
//         projectKey: TEST_PROJECT_KEY,
//       });

//       expect(http.delete).toHaveBeenCalledWith(
//         `${WORKFLOW_ENDPOINTS.DELETE}?id=${MOCK_WORKFLOW_ID_1}&projectKey=${TEST_PROJECT_KEY}`,
//       );
//       expect(result).toEqual(mockDeleteWorkflowResponse);
//     });
//   });

//   describe("getWorkflowExecutions", () => {
//     it("should call correct endpoint with URLSearchParams", async () => {
//       vi.mocked(http.get).mockResolvedValue(mockGetWorkflowExecutionsResponse);

//       const result = await workflowService.getWorkflowExecutions({
//         projectKey: TEST_PROJECT_KEY,
//         workflowId: MOCK_WORKFLOW_ID_1,
//       });

//       expect(http.get).toHaveBeenCalledWith(
//         `${WORKFLOW_ENDPOINTS.GET_EXECUTIONS}?ProjectKey=${TEST_PROJECT_KEY}&WorkflowId=${MOCK_WORKFLOW_ID_1}`,
//       );
//       expect(result).toEqual(mockGetWorkflowExecutionsResponse);
//     });
//   });

//   describe("getWorkflowExecutionById", () => {
//     it("should call correct endpoint with URLSearchParams", async () => {
//       vi.mocked(http.get).mockResolvedValue(mockGetWorkflowExecutionByIdResponse);

//       const result = await workflowService.getWorkflowExecutionById({
//         projectKey: TEST_PROJECT_KEY,
//         executionId: MOCK_WORKFLOW_EXECUTION_ID_1,
//       });

//       expect(http.get).toHaveBeenCalledWith(
//         `${WORKFLOW_ENDPOINTS.GET_EXECUTION}?ProjectKey=${TEST_PROJECT_KEY}&ExecutionId=${MOCK_WORKFLOW_EXECUTION_ID_1}`,
//       );
//       expect(result).toEqual(mockGetWorkflowExecutionByIdResponse);
//     });
//   });
// });
