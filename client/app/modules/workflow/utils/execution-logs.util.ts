import type { ExecutedNode } from "../models/workflow.model";
import type { ExecutionLogEntry, ExecutionLogLevel } from "../types/workflow.service.type";

export const EXECUTION_LOG_LEVELS: ExecutionLogLevel[] = ["Information", "Warning", "Error"];

export const EXECUTION_LOG_LEVEL_LABELS: Record<ExecutionLogLevel, string> = {
  Information: "Info",
  Warning: "Warning",
  Error: "Error",
};

/** Node filter values: every line, only execution-level lines, one node (all runs), or one run of a node. */
export const NODE_FILTER_ALL = "all";
export const NODE_FILTER_WORKFLOW = "workflow";
export const nodeFilterValue = (nodeId: string, runIndex?: number | null) =>
  runIndex == null ? `node:${nodeId}` : `run:${nodeId}#${runIndex}`;

export interface ExecutionLogFilters {
  levels: ExecutionLogLevel[];
  node: string;
  search: string;
}

export const DEFAULT_EXECUTION_LOG_FILTERS: ExecutionLogFilters = {
  levels: [...EXECUTION_LOG_LEVELS],
  node: NODE_FILTER_ALL,
  search: "",
};

export const isDefaultExecutionLogFilters = (filters: ExecutionLogFilters) =>
  filters.levels.length === EXECUTION_LOG_LEVELS.length &&
  filters.node === NODE_FILTER_ALL &&
  filters.search.trim() === "";

/** Unknown levels (a future backend) count as Information so they are never silently hidden. */
export const normalizeLevel = (level: string): ExecutionLogLevel =>
  (EXECUTION_LOG_LEVELS as string[]).includes(level) ? (level as ExecutionLogLevel) : "Information";

const matchesNode = (entry: ExecutionLogEntry, node: string) => {
  if (node === NODE_FILTER_ALL) return true;
  if (node === NODE_FILTER_WORKFLOW) return !entry.nodeId;
  if (!entry.nodeId) return false;
  if (node.startsWith("node:")) return entry.nodeId === node.slice("node:".length);
  if (node.startsWith("run:")) return nodeFilterValue(entry.nodeId, entry.runIndex ?? null) === node;
  return true;
};

/** Level, node and text filters combine with AND. */
export const filterExecutionLogs = (logs: ExecutionLogEntry[], filters: ExecutionLogFilters) => {
  const needle = filters.search.trim().toLowerCase();
  return logs.filter((entry) => {
    if (!filters.levels.includes(normalizeLevel(entry.level))) return false;
    if (!matchesNode(entry, filters.node)) return false;
    if (!needle) return true;
    return [entry.message, entry.stage, entry.nodeName ?? ""].some((field) =>
      field.toLowerCase().includes(needle),
    );
  });
};

export const countByLevel = (logs: ExecutionLogEntry[]) => {
  const counts: Record<ExecutionLogLevel, number> = { Information: 0, Warning: 0, Error: 0 };
  logs.forEach((entry) => {
    counts[normalizeLevel(entry.level)] += 1;
  });
  return counts;
};

export interface NodeFilterOption {
  value: string;
  label: string;
}

/**
 * "All nodes", "Workflow only", then every node seen in the logs or in the execution's node runs, in
 * first-appearance order. A node with more than one run also gets one option per run.
 */
export const buildNodeFilterOptions = (
  logs: ExecutionLogEntry[],
  nodeExecutions: Pick<ExecutedNode, "nodeId" | "nodeName" | "runIndex">[],
): NodeFilterOption[] => {
  const order: string[] = [];
  const names = new Map<string, string>();
  const runs = new Map<string, Set<number>>();

  const add = (nodeId: string, name?: string | null, runIndex?: number | null) => {
    if (!names.has(nodeId)) {
      order.push(nodeId);
      names.set(nodeId, name || nodeId);
      runs.set(nodeId, new Set());
    } else if (name && names.get(nodeId) === nodeId) {
      names.set(nodeId, name);
    }
    if (runIndex != null) runs.get(nodeId)!.add(runIndex);
  };

  logs.forEach((entry) => {
    if (entry.nodeId) add(entry.nodeId, entry.nodeName, entry.runIndex);
  });
  nodeExecutions.forEach((ne) => add(ne.nodeId, ne.nodeName, ne.runIndex));

  const options: NodeFilterOption[] = [
    { value: NODE_FILTER_ALL, label: "All nodes" },
    { value: NODE_FILTER_WORKFLOW, label: "Workflow only" },
  ];
  order.forEach((nodeId) => {
    const name = names.get(nodeId)!;
    options.push({ value: nodeFilterValue(nodeId), label: name });
    const nodeRuns = [...runs.get(nodeId)!].sort((a, b) => a - b);
    if (nodeRuns.length > 1) {
      nodeRuns.forEach((run) =>
        options.push({ value: nodeFilterValue(nodeId, run), label: `${name} · run ${run}` }),
      );
    }
  });
  return options;
};

const pad = (value: number, length = 2) => String(value).padStart(length, "0");

/** `HH:mm:ss.SSS` in local time. */
export const formatLogTime = (timestamp: string) => {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return timestamp;
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`;
};

export const logEntryNodeLabel = (entry: ExecutionLogEntry) =>
  entry.nodeId ? entry.nodeName || entry.nodeId : "Workflow";

/** One line per entry: `HH:mm:ss.SSS LEVEL [NodeName#run] message`. */
export const formatLogsForCopy = (logs: ExecutionLogEntry[]) =>
  logs
    .map((entry) => {
      const run = entry.nodeId && entry.runIndex != null ? `#${entry.runIndex}` : "";
      return `${formatLogTime(entry.timestamp)} ${normalizeLevel(entry.level).toUpperCase()} [${logEntryNodeLabel(entry)}${run}] ${entry.message}`;
    })
    .join("\n");
