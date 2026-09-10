import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import { IGetRunsPayload, ITestFunctionPayload, TERMINAL_RUN_STATUSES } from "../types/run.types";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";

const RUNS_QUERY_KEY = [FUNCTIONS_QUERY_KEY, "runs"];

export const useTestFunction = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...RUNS_QUERY_KEY, "test"],
    mutationFn: (payload: ITestFunctionPayload) => functionService.testFunction(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RUNS_QUERY_KEY });
      queryClient.invalidateQueries({ queryKey: [FUNCTIONS_QUERY_KEY] });
    },
  });
};

/** Runs list. Polls while the page shows any non-terminal run, so in-flight runs settle without a manual refresh. */
export const useGetRuns = (payload: IGetRunsPayload) => {
  return useQuery({
    queryKey: [...RUNS_QUERY_KEY, payload],
    queryFn: () => functionService.getRuns(payload),
    refetchInterval: (query) => {
      const runs = query.state.data?.data ?? [];
      const hasActiveRun = runs.some((run) => !TERMINAL_RUN_STATUSES.includes(run.status));
      return hasActiveRun ? 3000 : false;
    },
  });
};

export interface IGetRunOptions {
  runId?: string;
  enabled?: boolean;
}

/** A single run's detail. Polls every 2s until it reaches a terminal status. */
export const useGetRun = ({ runId, enabled = true }: IGetRunOptions) => {
  return useQuery({
    queryKey: [...RUNS_QUERY_KEY, "detail", runId],
    queryFn: () => functionService.getRun(runId!),
    enabled: enabled && !!runId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status && !TERMINAL_RUN_STATUSES.includes(status) ? 2000 : false;
    },
  });
};

export const useGetRunLogs = (runId: string | undefined, pageNumber = 0, pageSize = 200) => {
  return useQuery({
    queryKey: [...RUNS_QUERY_KEY, "logs", runId, pageNumber, pageSize],
    queryFn: () => functionService.getRunLogs(runId!, pageNumber, pageSize),
    enabled: !!runId,
  });
};

export const useReplayRun = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...RUNS_QUERY_KEY, "replay"],
    mutationFn: (runId: string) => functionService.replayRun(runId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RUNS_QUERY_KEY });
    },
  });
};

export const useCancelRun = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [...RUNS_QUERY_KEY, "cancel"],
    mutationFn: (runId: string) => functionService.cancelRun(runId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: RUNS_QUERY_KEY });
    },
  });
};
