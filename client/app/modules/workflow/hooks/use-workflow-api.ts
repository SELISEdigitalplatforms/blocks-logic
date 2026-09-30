import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { useWorkflowStore } from "../store";
import { useWorkflow } from "./use-workflow";
import { workflowService } from "../services/workflow.service";
import {
  IGetWorkflowsPayload,
  IGetWorkflowByIdPayload,
  IGetWorkflowExecutionsPayload,
  IGetWorkflowExecutionByIdPayload,
  IGetWorkflowVersionsPayload,
  IGetWorkflowByVersionPayload,
  IGetLastSuccessfulExecutionPayload,
  WorkflowExecution,
} from "../types/workflow.service.type";
import { showErrorToast } from "@/hooks/use-toast";
import { WORKFLOW_EXECUTION_PAGE_SIZE } from "../constants";
import { WorkflowExecutionStatus } from "../utils/workflow-execution-list.util";

export const useGetWorkflows = (options: IGetWorkflowsPayload) => {
  return useQuery({
    queryKey: ["workflows", options],
    queryFn: () => workflowService.getWorkflows(options),
  });
};

export const useGetWorkflowById = (payload: IGetWorkflowByIdPayload) => {
  return useQuery({
    queryKey: ["workflow", payload],
    queryFn: () => workflowService.getWorkflowById(payload),
    enabled: !!payload.id,
  });
};

export const useCreateWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "create"],
    mutationFn: workflowService.createWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
    },
  });
};

export const useDuplicateWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "duplicate"],
    mutationFn: workflowService.duplicateWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
    },
  });
};

export const useUpdateWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "update"],
    mutationFn: workflowService.updateWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
    },
  });
};

export const useDeleteWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "delete"],
    mutationFn: workflowService.deleteWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
    },
  });
};

export const useGetWorkflowExecutions = (
  payload: IGetWorkflowExecutionsPayload,
) => {
  return useQuery({
    queryKey: ["workflow-executions", payload],
    queryFn: () => workflowService.getWorkflowExecutions(payload),
    enabled: !!payload.workflowId,
    refetchInterval: 5000,
  });
};

export const useGetWorkflowExecutionById = (
  payload: IGetWorkflowExecutionByIdPayload,
) => {
  return useQuery({
    queryKey: ["workflow-execution", payload],
    queryFn: () => workflowService.getWorkflowExecutionById(payload),
    enabled: !!payload.executionId,
    refetchInterval: 5000,
  });
};

export const useCreateWorkflowVersion = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow-version", "create"],
    mutationFn: workflowService.createWorkflowVersion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};

export const useGetWorkflowVersions = (payload: IGetWorkflowVersionsPayload) => {
  return useQuery({
    queryKey: ["workflow-versions", payload],
    queryFn: () => workflowService.getWorkflowVersions(payload),
    enabled: !!payload.workflowId,
  });
};

export const useGetWorkflowByVersion = (payload: IGetWorkflowByVersionPayload) => {
  return useQuery({
    queryKey: ["workflow-version", payload],
    queryFn: () => workflowService.getWorkflowByVersion(payload),
    enabled: !!payload.workflowId && !!payload.versionId,
  });
};

export const usePublishWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "publish"],
    mutationFn: workflowService.publishWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};
export const usePublishNewWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "publish"],
    mutationFn: workflowService.publishWorkflowNewVersion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};

export const useUnpublishWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "unpublish"],
    mutationFn: workflowService.unpublishWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};

export const useRestoreWorkflow = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow", "restore"],
    mutationFn: workflowService.restoreWorkflow,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflows"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
    },
  });
};

export const useUpdateWorkflowVersion = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["workflow-version", "update"],
    mutationFn: workflowService.updateWorkflowVersion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["workflow-versions"] });
      queryClient.invalidateQueries({ queryKey: ["workflow"] });
    },
  });
};

export const useStepExecutionHandler = () => {
  const queryClient = useQueryClient();
  const { setStepExecutionData } = useWorkflow();

  const handleExecuteStep = async (executionId: string) => {
    try {
      const data = await queryClient.fetchQuery({
        queryKey: ["workflow-execution", { executionId }],
        queryFn: () => workflowService.getWorkflowExecutionById({ executionId }),
      });
      if (data) {
        setStepExecutionData(data);
      }
    } catch (e) {
      showErrorToast({ errors: (e instanceof Error ? e.message : "") || "Failed to fetch step execution" });
    }
  };

  return { handleExecuteStep };
};

export const useGetLastSuccessfulExecution = (
  payload: IGetLastSuccessfulExecutionPayload,
) => {
  return useQuery({
    queryKey: ["workflow-last-successful-execution", payload],
    queryFn: () => workflowService.getLastSuccessfulExecution(payload),
    enabled: !!payload.workflowId,
  });
};

export const useStepExecute = () => {
  return useMutation({
    mutationKey: ["workflow", "step-execute"],
    mutationFn: workflowService.stepExecute,
  });
};

export const useExecuteTriggerListener = () => {
  const workflowId = useWorkflowStore((state) => state.workflowId);

  return useMutation({
    mutationKey: ["workflow", "trigger-listener"],
    mutationFn: async ({ triggerId, enableListener, completionNodeId }: { triggerId: string; enableListener: boolean; completionNodeId?: string }) => {
      if (!workflowId) return;
      return workflowService.triggerListener({
        WorkflowId: workflowId,
        TriggerId: triggerId,
        EnableListener: enableListener,
        ...(completionNodeId && { CompletionNodeId: completionNodeId }),
      });
    },
  });
};

export const useEnqueueWorkflowImport = () => {
  return useMutation({
    mutationKey: ["workflow", "import"],
    mutationFn: workflowService.importWorkflow,
  });
};

const EXECUTION_POLL_MS = 5000;
const MAX_NEWER_ROUNDS = 5;
const MAX_REFRESH_IDS = 50;

const NON_TERMINAL_STATUSES = new Set<number>([
  WorkflowExecutionStatus.Init,
  WorkflowExecutionStatus.Queued,
  WorkflowExecutionStatus.Pending,
  WorkflowExecutionStatus.Running,
]);

type NewerPull = {
  collected: WorkflowExecution[];
  refreshed: WorkflowExecution[];
  workflowId: string;
};

const pullNewerExecutions = async (
  workflowId: string,
  headId: string,
  refreshIds: string[],
): Promise<NewerPull> => {
  const collected: WorkflowExecution[] = [];
  const refreshed: WorkflowExecution[] = [];
  let afterId = headId;

  for (let round = 0; round < MAX_NEWER_ROUNDS; round += 1) {
    const response = await workflowService.getWorkflowExecutions({
      workflowId,
      pageSize: WORKFLOW_EXECUTION_PAGE_SIZE,
      afterId,
      refreshIds: round === 0 && refreshIds.length > 0 ? refreshIds : undefined,
    });
    if (round === 0 && response.refreshed?.length) {
      refreshed.push(...response.refreshed);
    }
    const chunk = response.data ?? [];
    collected.push(...chunk);
    if (chunk.length < WORKFLOW_EXECUTION_PAGE_SIZE) {
      break;
    }
    const newestInChunk = chunk[chunk.length - 1];
    if (!newestInChunk?.id) {
      break;
    }
    afterId = newestInChunk.id;
  }

  return { collected, refreshed, workflowId };
};

const sameStatusFields = (row: WorkflowExecution, update: WorkflowExecution) =>
  update.status === row.status &&
  (update.finishedAt ?? null) === (row.finishedAt ?? null) &&
  (update.errorMessage ?? null) === (row.errorMessage ?? null) &&
  (update.attemptNumber ?? null) === (row.attemptNumber ?? null);

/** Patches in-flight rows in place and prepends unknown ids, newest first. */
export const mergeExecutionRows = (
  current: WorkflowExecution[],
  collected: WorkflowExecution[],
  refreshed: WorkflowExecution[],
): { next: WorkflowExecution[]; prepended: number } => {
  const refreshById = new Map(refreshed.map((row) => [row.id, row]));
  let patched = false;
  const updated = current.map((row) => {
    const update = refreshById.get(row.id);
    if (!update || sameStatusFields(row, update)) {
      return row;
    }
    patched = true;
    return {
      ...row,
      status: update.status,
      finishedAt: update.finishedAt ?? row.finishedAt,
      errorMessage: update.errorMessage ?? row.errorMessage,
      attemptNumber: update.attemptNumber ?? row.attemptNumber,
    };
  });

  const seen = new Set(updated.map((row) => row.id));
  const fresh = collected.filter((row) => row.id && !seen.has(row.id));
  if (fresh.length === 0) {
    return patched ? { next: updated, prepended: 0 } : { next: current, prepended: 0 };
  }

  return { next: [...fresh].reverse().concat(updated), prepended: fresh.length };
};

export const useWorkflowExecutionHistory = (workflowId: string) => {
  const pageSize = WORKFLOW_EXECUTION_PAGE_SIZE;
  const [rows, setRows] = useState<WorkflowExecution[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [initialSettled, setInitialSettled] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [prependCount, setPrependCount] = useState(0);

  const rowsRef = useRef<WorkflowExecution[]>([]);
  const refreshIdsRef = useRef<string[]>([]);
  const loadingMoreRef = useRef(false);
  const snapshotCountRef = useRef(0);
  const totalCountRef = useRef(0);
  const appliedForWorkflow = useRef<string | null>(null);
  const mergedAtRef = useRef(0);
  const seenWorkflow = useRef(workflowId);

  const switched = seenWorkflow.current !== workflowId;
  if (switched) {
    seenWorkflow.current = workflowId;
    snapshotCountRef.current = 0;
    totalCountRef.current = 0;
    appliedForWorkflow.current = null;
    mergedAtRef.current = 0;
    setRows([]);
    setHasMore(false);
    setInitialSettled(false);
    setPrependCount(0);
    setIsLoadingMore(false);
  }

  rowsRef.current = switched ? [] : rows;
  refreshIdsRef.current = rowsRef.current
    .filter((row) => NON_TERMINAL_STATUSES.has(row.status))
    .slice(0, MAX_REFRESH_IDS)
    .map((row) => row.id);

  const initialQuery = useQuery({
    queryKey: ["workflow-executions", workflowId, "page", pageSize],
    queryFn: () => workflowService.getWorkflowExecutions({ workflowId, pageSize }),
    enabled: !!workflowId,
    refetchOnWindowFocus: false,
    refetchInterval: (query) => {
      const page = query.state.data?.data;
      if (!page || page.length === 0) return EXECUTION_POLL_MS;
      return false;
    },
  });

  const newerQuery = useQuery({
    queryKey: ["workflow-executions-newer", workflowId],
    queryFn: () => {
      const headId = rowsRef.current[0]?.id;
      if (!headId) {
        return Promise.resolve({
          collected: [],
          refreshed: [],
          workflowId,
        } satisfies NewerPull);
      }
      return pullNewerExecutions(workflowId, headId, refreshIdsRef.current);
    },
    enabled: !!workflowId && !switched && rows.length > 0 && initialSettled,
    refetchOnWindowFocus: false,
    refetchInterval: EXECUTION_POLL_MS,
  });

  useEffect(() => {
    if (!workflowId || !initialQuery.isFetched) return;
    if (initialQuery.isError) {
      setInitialSettled(true);
      return;
    }
    if (appliedForWorkflow.current === workflowId && rowsRef.current.length > 0) {
      setInitialSettled(true);
      return;
    }

    const data = initialQuery.data?.data ?? [];
    const total = initialQuery.data?.totalCount ?? data.length;
    snapshotCountRef.current = data.length;
    totalCountRef.current = total;
    setRows((current) => (current.length === 0 && data.length === 0 ? current : data));
    setHasMore(data.length === pageSize && data.length < total);
    appliedForWorkflow.current = workflowId;
    setInitialSettled(true);
  }, [
    workflowId,
    initialQuery.isFetched,
    initialQuery.isError,
    initialQuery.data,
    initialQuery.dataUpdatedAt,
    pageSize,
  ]);

  useEffect(() => {
    const pull = newerQuery.data;
    if (!pull || pull.workflowId !== workflowId || newerQuery.dataUpdatedAt === mergedAtRef.current) {
      return;
    }
    mergedAtRef.current = newerQuery.dataUpdatedAt;
    const { next, prepended } = mergeExecutionRows(
      rowsRef.current,
      pull.collected,
      pull.refreshed,
    );
    if (next === rowsRef.current) return;
    if (prepended > 0) setPrependCount((count) => count + 1);
    setRows(next);
  }, [newerQuery.data, newerQuery.dataUpdatedAt, workflowId]);

  const loadMore = useCallback(async () => {
    if (!workflowId || loadingMoreRef.current || !hasMore) return;
    const oldest = rowsRef.current[rowsRef.current.length - 1];
    if (!oldest) return;

    loadingMoreRef.current = true;
    setIsLoadingMore(true);
    try {
      const response = await workflowService.getWorkflowExecutions({
        workflowId,
        pageSize,
        beforeId: oldest.id,
      });
      const page = response.data ?? [];
      const seen = new Set(rowsRef.current.map((row) => row.id));
      const appended = page.filter((row) => row.id && !seen.has(row.id));
      const next = appended.length > 0 ? [...rowsRef.current, ...appended] : rowsRef.current;
      rowsRef.current = next;
      snapshotCountRef.current += appended.length;
      setRows(next);
      setHasMore(page.length === pageSize && snapshotCountRef.current < totalCountRef.current);
    } catch {
      // Keep the rows already on screen.
    } finally {
      loadingMoreRef.current = false;
      setIsLoadingMore(false);
    }
  }, [workflowId, hasMore, pageSize]);

  return {
    rows: switched ? [] : rows,
    hasMore: switched ? false : hasMore,
    isLoading: !!workflowId && (switched || (!initialSettled && initialQuery.isLoading)),
    isLoadingMore,
    loadMore,
    prependCount,
  };
};
