import { useEffect, useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { functionService } from "../services/function.service";
import { IGetRunsPayload, ITestFunctionPayload, TERMINAL_RUN_STATUSES } from "../types/run.types";
import { FUNCTIONS_QUERY_KEY } from "./use-functions";
import { runPollInterval } from "../utils/run-polling";

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

/**
 * Runs list. Polls every 5 s while it holds a non-terminal run younger than the cutoff, so in-flight runs settle
 * without a manual refresh, backing off as the youngest of them ages (see `runPollInterval`);
 * `autoRefresh: false` is the runs tab's toggle turned off.
 */
export const useGetRuns = (
  payload: IGetRunsPayload,
  options?: { autoRefresh?: boolean; enabled?: boolean },
) => {
  const autoRefresh = options?.autoRefresh ?? true;
  return useQuery({
    queryKey: [...RUNS_QUERY_KEY, payload],
    queryFn: () => functionService.getRuns(payload),
    // Lets the caller hold the query until every part of the key is settled, so a window that is
    // computed in an effect does not cost a first fetch with the wrong bound.
    enabled: options?.enabled ?? true,
    // Coming back to the tab refreshes the list only where it is live on screen.
    refetchOnWindowFocus: autoRefresh,
    refetchInterval: (query) => {
      if (!autoRefresh) return false;
      const runs = query.state.data?.data ?? [];
      // The most eager interval wins: one fresh run keeps the list responsive even when an old
      // stuck one alone would have stopped it.
      const intervals = runs
        .filter((run) => !TERMINAL_RUN_STATUSES.includes(run.status))
        .map((run) => runPollInterval(run.createdDate, 5000))
        .filter((interval): interval is number => interval !== false);
      return intervals.length > 0 ? Math.min(...intervals) : false;
    },
  });
};

export interface IGetRunOptions {
  runId?: string;
  enabled?: boolean;
  /**
   * Whether to follow the run until it settles. Off for a caller that only reads what the run
   * was given (its input), which never changes — polling it only multiplied requests.
   */
  poll?: boolean;
}

/**
 * A single run's detail. Polls every 2 s until it reaches a terminal status, backing off with age.
 * When a followed run settles, the runs list is refreshed once — so the list does not have to
 * poll alongside it to notice.
 */
export const useGetRun = ({ runId, enabled = true, poll = true }: IGetRunOptions) => {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: [...RUNS_QUERY_KEY, "detail", runId],
    queryFn: () => functionService.getRun(runId!),
    enabled: enabled && !!runId,
    // A settled run never changes again, so refocusing the tab has nothing to fetch.
    refetchOnWindowFocus: (q) =>
      poll && !!q.state.data?.status && !TERMINAL_RUN_STATUSES.includes(q.state.data.status),
    refetchInterval: (q) => {
      if (!poll) return false;
      const run = q.state.data;
      if (!run?.status || TERMINAL_RUN_STATUSES.includes(run.status)) return false;
      return runPollInterval(run.createdDate, 2000);
    },
  });

  const status = query.data?.status;
  const previous = useRef(status);
  useEffect(() => {
    const was = previous.current;
    previous.current = status;
    if (!poll || !status || !was || was === status) return;
    if (!TERMINAL_RUN_STATUSES.includes(was) && TERMINAL_RUN_STATUSES.includes(status)) {
      // Only the lists — the keys whose third part is the list payload, not "detail"/"logs".
      queryClient.invalidateQueries({
        queryKey: RUNS_QUERY_KEY,
        predicate: (q) => typeof q.queryKey[2] === "object" && q.queryKey[2] !== null,
      });
    }
  }, [poll, status, queryClient]);

  return query;
};

export const useGetRunLogs = (runId: string | undefined, pageNumber = 0, pageSize = 200) => {
  return useQuery({
    queryKey: [...RUNS_QUERY_KEY, "logs", runId, pageNumber, pageSize],
    queryFn: () => functionService.getRunLogs(runId!, pageNumber, pageSize),
    enabled: !!runId,
  });
};

/**
 * The newest Test run of a function. The Test request stays open until its run ends, so this is
 * how the panel learns the run id early (live status + logs) and finds a test still running after
 * a reload or a 429. `fast` polls while a Test request is open.
 */
export const useLatestTestRun = (functionId: string, options?: { fast?: boolean }) => {
  return useQuery({
    queryKey: [...RUNS_QUERY_KEY, "latest-test", functionId],
    queryFn: async () => {
      const response = await functionService.getRuns({
        functionId,
        invokedBy: "Test",
        pageNumber: 0,
        pageSize: 1,
      });
      return response?.data?.[0] ?? null;
    },
    enabled: !!functionId,
    refetchOnWindowFocus: false,
    refetchInterval: options?.fast ? 1500 : false,
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
