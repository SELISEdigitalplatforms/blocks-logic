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
  useWorkflowExecutionHistory,
} from "./use-workflow-api";
import { WORKFLOW_EXECUTION_PAGE_SIZE } from "../constants";
import { WorkflowExecutionStatus } from "../utils/workflow-execution-list.util";
import { WorkflowExecution } from "../types/workflow.service.type";

vi.mock("../services/workflow.service", () => mockWorkflowServiceFactory());

describe("useGetWorkflows", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should return workflow list on success", async () => {
    vi.mocked(workflowService.getWorkflows).mockResolvedValue(mockGetWorkflowsResponse);

    const { result } = renderHook(() => useGetWorkflows({}), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowsResponse);
    expect(workflowService.getWorkflows).toHaveBeenCalledWith({});
  });
});

describe("useGetWorkflowById", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should return workflow detail on success", async () => {
    vi.mocked(workflowService.getWorkflowById).mockResolvedValue(mockGetWorkflowByIdResponse);

    const { result } = renderHook(
      () => useGetWorkflowById({ id: MOCK_WORKFLOW_ID_1 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowByIdResponse);
  });

  it("should not fetch when id is empty", () => {
    const { result } = renderHook(() => useGetWorkflowById({ id: "" }), {
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

    const payload = { name: "New Workflow" };
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

    const payload = { itemId: MOCK_WORKFLOW_ID_1 };
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

    const payload = { id: MOCK_WORKFLOW_ID_1 };
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
        useGetWorkflowExecutions({ workflowId: MOCK_WORKFLOW_ID_1 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowExecutionsResponse);
  });

  it("should not fetch when workflowId is empty", () => {
    const { result } = renderHook(
      () => useGetWorkflowExecutions({ workflowId: "" }),
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
          executionId: MOCK_WORKFLOW_EXECUTION_ID_1,
        }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockGetWorkflowExecutionByIdResponse);
  });

  it("should not fetch when executionId is empty", () => {
    const { result } = renderHook(
      () => useGetWorkflowExecutionById({ executionId: "" }),
      { wrapper: createWrapper() },
    );

    expect(result.current.isFetching).toBe(false);
    expect(workflowService.getWorkflowExecutionById).not.toHaveBeenCalled();
  });
});

const executionRow = (
  id: string,
  status: WorkflowExecutionStatus = WorkflowExecutionStatus.Completed,
  workflowId = "w1",
): WorkflowExecution => ({
  id,
  workflowId,
  status,
  executionMode: 1,
  startedAt: "2023-01-01T00:00:00Z",
  finishedAt: status === WorkflowExecutionStatus.Running ? "" : "2023-01-01T00:00:05Z",
  duration: 5,
  triggeredBy: "",
  errorMessage: "",
});

describe("useWorkflowExecutionHistory", () => {
  beforeEach(() => vi.clearAllMocks());

  it("stops when the first page is short", async () => {
    vi.mocked(workflowService.getWorkflowExecutions).mockImplementation(async (payload) => {
      if (payload.afterId) {
        return { data: [], refreshed: [], totalCount: 1, errors: null };
      }
      return { data: [executionRow("only")], totalCount: 1, errors: null };
    });

    const { result } = renderHook(() => useWorkflowExecutionHistory("w1"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.rows.map((row) => row.id)).toEqual(["only"]));
    expect(result.current.hasMore).toBe(false);

    await act(async () => {
      await result.current.loadMore();
    });

    expect(
      vi.mocked(workflowService.getWorkflowExecutions).mock.calls.some(
        ([payload]) => payload.beforeId,
      ),
    ).toBe(false);
  });

  it("appends an older page and ignores duplicate ids", async () => {
    const first = Array.from({ length: WORKFLOW_EXECUTION_PAGE_SIZE }, (_, index) =>
      executionRow(`new-${index}`),
    );
    vi.mocked(workflowService.getWorkflowExecutions).mockImplementation(async (payload) => {
      if (payload.afterId) {
        return { data: [], refreshed: [], totalCount: 22, errors: null };
      }
      if (payload.beforeId) {
        return {
          data: [first[first.length - 1], executionRow("old-1")],
          totalCount: 22,
          errors: null,
        };
      }
      return { data: first, totalCount: 22, errors: null };
    });

    const { result } = renderHook(() => useWorkflowExecutionHistory("w1"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.hasMore).toBe(true));
    await act(async () => {
      await result.current.loadMore();
    });

    expect(result.current.rows.map((row) => row.id)).toEqual([
      ...first.map((row) => row.id),
      "old-1",
    ]);
    expect(result.current.hasMore).toBe(false);
  });

  it("prepends newer runs and patches status without reordering", async () => {
    vi.mocked(workflowService.getWorkflowExecutions).mockImplementation(async (payload) => {
      if (payload.afterId) {
        return {
          data: [
            executionRow("head", WorkflowExecutionStatus.Running),
            executionRow("brand-new", WorkflowExecutionStatus.Running),
          ],
          refreshed: [executionRow("head", WorkflowExecutionStatus.Completed)],
          totalCount: 2,
          errors: null,
        };
      }
      return {
        data: [
          executionRow("head", WorkflowExecutionStatus.Running),
          executionRow("older"),
        ],
        totalCount: 2,
        errors: null,
      };
    });

    const { result } = renderHook(() => useWorkflowExecutionHistory("w1"), {
      wrapper: createWrapper(),
    });

    await waitFor(() =>
      expect(result.current.rows.map((row) => row.id)).toEqual(["brand-new", "head", "older"]),
    );
    expect(result.current.rows[1]?.status).toBe(WorkflowExecutionStatus.Completed);
    expect(result.current.rows[2]?.id).toBe("older");
  });

  it("resets the list when the workflow changes", async () => {
    vi.mocked(workflowService.getWorkflowExecutions).mockImplementation(async (payload) => {
      if (payload.afterId) {
        return { data: [], refreshed: [], totalCount: 1, errors: null };
      }
      return {
        data: [
          executionRow(
            payload.workflowId === "w1" ? "from-w1" : "from-w2",
            WorkflowExecutionStatus.Completed,
            payload.workflowId,
          ),
        ],
        totalCount: 1,
        errors: null,
      };
    });

    const { result, rerender } = renderHook(
      ({ id }: { id: string }) => useWorkflowExecutionHistory(id),
      { wrapper: createWrapper(), initialProps: { id: "w1" } },
    );

    await waitFor(() => expect(result.current.rows.map((row) => row.id)).toEqual(["from-w1"]));
    rerender({ id: "w2" });
    await waitFor(() => expect(result.current.rows.map((row) => row.id)).toEqual(["from-w2"]));
  });
});
