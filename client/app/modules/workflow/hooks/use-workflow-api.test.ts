import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { mockWorkflowServiceFactory } from "../test-utils/__mocks__";
import {
  mockGetWorkflowsResponse,
  mockGetWorkflowByIdResponse,
  mockCreateWorkflowResponse,
  mockDuplicateWorkflowResponse,
  mockUpdateWorkflowResponse,
  mockDeleteWorkflowResponse,
  mockGetWorkflowExecutionsResponse,
  mockGetWorkflowExecutionByIdResponse,
  MOCK_WORKFLOW_ID_1,
  MOCK_WORKFLOW_EXECUTION_ID_1,
} from "../test-utils/__mocks__";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import { workflowService } from "../services/workflow.service";
import {
  useGetWorkflows,
  useGetWorkflowById,
  useCreateWorkflow,
  useDuplicateWorkflow,
  useUpdateWorkflow,
  useDeleteWorkflow,
  useGetWorkflowExecutions,
  useGetWorkflowExecutionById,
} from "./use-workflow-api";

vi.mock("../services/workflow.service", () => mockWorkflowServiceFactory());

describe("useGetWorkflows", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should return workflow list on success", async () => {
    vi.mocked(workflowService.getWorkflows).mockResolvedValue(mockGetWorkflowsResponse);

    const { result } = renderHook(() => useGetWorkflows({ projectKey: TEST_PROJECT_KEY }), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowsResponse);
    expect(workflowService.getWorkflows).toHaveBeenCalledWith({ projectKey: TEST_PROJECT_KEY });
  });
});

describe("useGetWorkflowById", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should return workflow detail on success", async () => {
    vi.mocked(workflowService.getWorkflowById).mockResolvedValue(mockGetWorkflowByIdResponse);

    const { result } = renderHook(
      () => useGetWorkflowById({ id: MOCK_WORKFLOW_ID_1, projectKey: TEST_PROJECT_KEY }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowByIdResponse);
  });

  it("should not fetch when id and projectKey are both empty", () => {
    const { result } = renderHook(() => useGetWorkflowById({ id: "", projectKey: "" }), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
    expect(workflowService.getWorkflowById).not.toHaveBeenCalled();
  });
});

describe("useCreateWorkflow", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should call createWorkflow service method and return response", async () => {
    vi.mocked(workflowService.createWorkflow).mockResolvedValue(mockCreateWorkflowResponse);

    const { result } = renderHook(() => useCreateWorkflow(), { wrapper: createWrapper() });

    const payload = { name: "New Workflow", projectKey: TEST_PROJECT_KEY };
    await act(async () => {
      await result.current.mutateAsync(payload);
    });

    expect(workflowService.createWorkflow).toHaveBeenCalledWith(payload, expect.anything());
    await waitFor(() => expect(result.current.data).toEqual(mockCreateWorkflowResponse));
  });
});

describe("useDuplicateWorkflow", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should call duplicateWorkflow service method and return response", async () => {
    vi.mocked(workflowService.duplicateWorkflow).mockResolvedValue(mockDuplicateWorkflowResponse);

    const { result } = renderHook(() => useDuplicateWorkflow(), { wrapper: createWrapper() });

    const payload = {
      name: "Copy of Workflow",
      workflowId: MOCK_WORKFLOW_ID_1,
      projectKey: TEST_PROJECT_KEY,
    };
    await act(async () => {
      await result.current.mutateAsync(payload);
    });

    expect(workflowService.duplicateWorkflow).toHaveBeenCalledWith(payload, expect.anything());
    await waitFor(() => expect(result.current.data).toEqual(mockDuplicateWorkflowResponse));
  });
});

describe("useUpdateWorkflow", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should call updateWorkflow service method and return response", async () => {
    vi.mocked(workflowService.updateWorkflow).mockResolvedValue(mockUpdateWorkflowResponse);

    const { result } = renderHook(() => useUpdateWorkflow(), { wrapper: createWrapper() });

    const payload = { itemId: MOCK_WORKFLOW_ID_1, projectKey: TEST_PROJECT_KEY };
    await act(async () => {
      await result.current.mutateAsync(payload);
    });

    expect(workflowService.updateWorkflow).toHaveBeenCalledWith(payload, expect.anything());
    await waitFor(() => expect(result.current.data).toEqual(mockUpdateWorkflowResponse));
  });
});

describe("useDeleteWorkflow", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should call deleteWorkflow service method and return response", async () => {
    vi.mocked(workflowService.deleteWorkflow).mockResolvedValue(mockDeleteWorkflowResponse);

    const { result } = renderHook(() => useDeleteWorkflow(), { wrapper: createWrapper() });

    const payload = { id: MOCK_WORKFLOW_ID_1, projectKey: TEST_PROJECT_KEY };
    await act(async () => {
      await result.current.mutateAsync(payload);
    });

    expect(workflowService.deleteWorkflow).toHaveBeenCalledWith(payload, expect.anything());
    await waitFor(() => expect(result.current.data).toEqual(mockDeleteWorkflowResponse));
  });
});

describe("useGetWorkflowExecutions", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should return executions list on success", async () => {
    vi.mocked(workflowService.getWorkflowExecutions).mockResolvedValue(
      mockGetWorkflowExecutionsResponse,
    );

    const { result } = renderHook(
      () =>
        useGetWorkflowExecutions({ projectKey: TEST_PROJECT_KEY, workflowId: MOCK_WORKFLOW_ID_1 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowExecutionsResponse);
  });

  it("should not fetch when workflowId is empty", () => {
    const { result } = renderHook(
      () => useGetWorkflowExecutions({ projectKey: TEST_PROJECT_KEY, workflowId: "" }),
      { wrapper: createWrapper() },
    );

    expect(result.current.isFetching).toBe(false);
    expect(workflowService.getWorkflowExecutions).not.toHaveBeenCalled();
  });
});

describe("useGetWorkflowExecutionById", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should return execution detail on success", async () => {
    vi.mocked(workflowService.getWorkflowExecutionById).mockResolvedValue(
      mockGetWorkflowExecutionByIdResponse,
    );

    const { result } = renderHook(
      () =>
        useGetWorkflowExecutionById({
          projectKey: TEST_PROJECT_KEY,
          executionId: MOCK_WORKFLOW_EXECUTION_ID_1,
        }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowExecutionByIdResponse);
  });

  it("should not fetch when executionId is empty", () => {
    const { result } = renderHook(
      () => useGetWorkflowExecutionById({ projectKey: TEST_PROJECT_KEY, executionId: "" }),
      { wrapper: createWrapper() },
    );

    expect(result.current.isFetching).toBe(false);
    expect(workflowService.getWorkflowExecutionById).not.toHaveBeenCalled();
  });
});
