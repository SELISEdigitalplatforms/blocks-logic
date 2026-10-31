import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { useWorkflowStore } from "@blocks-workflow/store";
import { useCallback, useMemo, useState } from "react";
import { inferSchemaFromRuntimeRows } from "@blocks-workflow/utils/runtime-node-data";
import { ChevronDown, ChevronUp, Pencil, Pin, PinOff, TriangleAlert } from "lucide-react";
import { useWorkflow } from "@blocks-workflow/hooks/use-workflow";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { showErrorToast } from "@/hooks/use-toast";
import { NodeExecutionStatus } from "@blocks-workflow/utils/workflow-execution-editor.util";
import type { ExecutedNode } from "@blocks-workflow/models/workflow.model";
import { CopyValueButton } from "../copy-value-button";
import { itemCountLabel, outputBranchGroups, showsBranchHeaders, type OutputBranchGroup } from "@blocks-workflow/utils/output-branches";
import { fieldsNotFound } from "@blocks-workflow/utils/split-out";
import { mergeInputCounts, mergeInputsWithoutData } from "@blocks-workflow/utils/merge-handles";

type BranchGroup = OutputBranchGroup & {
  schema: ReturnType<typeof inferSchemaFromRuntimeRows>;
};

/** A branch's name (its output's label, not its key), item count, and a copy button for the key. */
const BranchHeader = ({ group, className = "" }: { group: BranchGroup; className?: string }) => (
  <div className={`group/branch flex min-w-0 items-center gap-2 ${className}`}>
    <span title={group.key} className="truncate text-xs font-semibold text-high-emphasis">
      {group.title}
    </span>
    <span className="shrink-0 text-xs text-low-emphasis">{itemCountLabel(group.rows.length)}</span>
    <CopyValueButton
      value={group.key}
      label={`Copy branch key of ${group.title}`}
      className="-my-0.5 ml-auto h-5 shrink-0 px-1 opacity-0 focus-visible:opacity-100 group-hover/branch:opacity-100"
      iconClassName="h-3.5 w-3.5"
    />
  </div>
);

export const OutputPanel = ({
  isCollapsed,
  onToggleCollapse,
}: {
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
} = {}) => {
  const [tab, setTab] = useState("schema");
  const [isEditingMock, setIsEditingMock] = useState(false);
  const [mockDataInput, setMockDataInput] = useState("");
  const selectedNode = useWorkflowStore((s) => s.selectedNode);
  const executedItems = useWorkflowStore((s) => s.executedItems);
  const executedNodes = useWorkflowStore((s) => s.executedNodes);
  const edgesMap = useWorkflowStore((s) => s.edgesMap);
  const { editorMode, updateNode, lastSuccessfulExecutionData } = useWorkflow();
  const hasMockData = selectedNode?.pinData != null;

  const runtimeOutputByBranch = useMemo<BranchGroup[]>(() => {
    if (!selectedNode) return [];

    // Mock data replaces the step's output, so while it is set it is what the panel shows.
    if (selectedNode.pinData != null) {
      const rows = Array.isArray(selectedNode.pinData) ? selectedNode.pinData : [selectedNode.pinData];
      return [
        {
          key: "default",
          title: "default",
          rows,
          schema: inferSchemaFromRuntimeRows(rows),
        },
      ];
    }

    const groupedRows = executedItems
      .filter((item) => item.nodeId === selectedNode.id)
      .sort((a, b) => a.itemIndex - b.itemIndex)
      .reduce<Map<string, unknown[]>>((acc, item) => {
        const branch = item.branch || "default";
        const existingRows = acc.get(branch) ?? [];

        existingRows.push(item.data?.Output);
        acc.set(branch, existingRows);

        return acc;
      }, new Map<string, unknown[]>());

    // Nothing ran yet: no groups, so every tab shows its usual empty state.
    if (groupedRows.size === 0) return [];
    return outputBranchGroups(selectedNode, groupedRows).map((group) => ({
      ...group,
      schema: inferSchemaFromRuntimeRows(group.rows),
    }));
  }, [executedItems, selectedNode]);

  // The error the server saved on this node's latest run (nodeExecution.error). It used to be stored
  // and never shown, so a failed test step looked like one with no output (WF-40).
  // Split Out: how many items went in and came out, and the fields no input item had (n8n's hint).
  const splitOutSummary = useMemo(() => {
    if (!selectedNode || selectedNode.type !== "splitOut" || selectedNode.pinData != null) return null;
    const run = latestRun(executedNodes, selectedNode.id);
    if (!run || run.status === NodeExecutionStatus.Failed) return null;
    const parameters = (selectedNode.parameters ?? {}) as Record<string, unknown>;
    const input = Array.isArray(run.input) ? run.input : [];
    return {
      inputCount: run.inputItemCount ?? input.length,
      outputCount: run.outputItemCount ?? 0,
      notFound: fieldsNotFound(parameters.fields, input, parameters.disableDotNotation !== true),
    };
  }, [executedNodes, selectedNode]);

  // Limit: how many items went in and how many were kept.
  const limitSummary = useMemo(() => {
    if (selectedNode?.type !== "limit" || selectedNode.pinData != null) return null;
    const run = latestRun(executedNodes, selectedNode.id);
    if (!run || run.status === NodeExecutionStatus.Failed) return null;
    const input = Array.isArray(run.input) ? run.input : [];
    return {
      inputCount: run.inputItemCount ?? input.length,
      outputCount: run.outputItemCount ?? 0,
    };
  }, [executedNodes, selectedNode]);

  // Merge: how many items each input received on the latest run.
  const mergeSummary = useMemo(() => {
    if (!selectedNode || selectedNode.type !== "merge" || selectedNode.pinData != null) return null;
    const run = latestRun(executedNodes, selectedNode.id);
    if (!run || run.status === NodeExecutionStatus.Failed) return null;
    const parameters = (selectedNode.parameters ?? {}) as Record<string, unknown>;
    const inputs = mergeInputCounts(selectedNode.id, parameters.numberInputs, Object.values(edgesMap), (sourceId) => {
      const source = latestRun(executedNodes, sourceId);
      return source?.status === NodeExecutionStatus.Completed ? source.outputCountsByBranch : undefined;
    });
    return { inputs, withoutData: mergeInputsWithoutData(parameters.runWhen, inputs) };
  }, [edgesMap, executedNodes, selectedNode]);

  const failedRunError = useMemo(
    () => (selectedNode ? latestRunError(executedNodes, selectedNode.id) : null),
    [executedNodes, selectedNode],
  );

  const runtimeOutputRows = useMemo(
    () => runtimeOutputByBranch.flatMap((group) => group.rows),
    [runtimeOutputByBranch],
  );

  const hasMultipleBranches = showsBranchHeaders(runtimeOutputByBranch);

  const handleSetMockDataClick = useCallback(() => {
    setIsEditingMock(true);
    
    let defaultData: unknown[] = [];
    
    if (selectedNode?.pinData != null) {
      defaultData = Array.isArray(selectedNode.pinData) ? selectedNode.pinData : [selectedNode.pinData];
    } else if (selectedNode && lastSuccessfulExecutionData?.data?.items) {
      const matchedNodeItem = lastSuccessfulExecutionData.data.items.find(
        (item: { nodeId: string; data?: { Output?: unknown } }) => item.nodeId === selectedNode.id
      );
      
      if (matchedNodeItem?.data?.Output !== undefined) {
        const output = matchedNodeItem.data.Output;
        defaultData = Array.isArray(output) ? output : [output];
      }
    }

    setMockDataInput(JSON.stringify(defaultData, null, 2));
  }, [selectedNode, lastSuccessfulExecutionData]);

  const handleCancelMockData = useCallback(() => {
    setIsEditingMock(false);
  }, []);

  const handleSaveMockData = useCallback(() => {
    if (!selectedNode) return;
    let newPinData;
    try {
      newPinData = JSON.parse(mockDataInput);
    } catch {
      showErrorToast({ errors: "Invalid JSON format" });
      return;
    }
    if (!Array.isArray(newPinData)) {
      newPinData = [newPinData];
    }
    updateNode(selectedNode.id, { pinData: newPinData });
    setIsEditingMock(false);
  }, [mockDataInput, selectedNode, updateNode]);

  const handleRemoveMockData = useCallback(() => {
    if (!selectedNode) return;
    updateNode(selectedNode.id, { pinData: null });
  }, [selectedNode, updateNode]);

  if (!selectedNode) return null;

  return (
    <div className={`flex w-full flex-col overflow-hidden ${isCollapsed ? 'h-fit shrink-0' : 'h-full flex-1'}`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="font-medium text-high-emphasis">Output</h3>
          {hasMockData && (
            <Tooltip>
              <TooltipTrigger asChild>
                <Badge variant="warning" className="gap-1 px-1.5 py-0.5 text-[11px]">
                  <Pin className="h-3 w-3" aria-hidden="true" />
                  Mock data
                </Badge>
              </TooltipTrigger>
              <TooltipContent className="text-xs">
                Showing mock data. Runs use it as this step&apos;s output instead of running the step.
              </TooltipContent>
            </Tooltip>
          )}
        </div>
        <div className="flex items-center gap-2">
          {!isEditingMock && editorMode === "editor" && !hasMockData && (
            <Button variant="outline" size="xs" onClick={handleSetMockDataClick}>
              Set mock data
            </Button>
          )}
          {!isEditingMock && editorMode === "editor" && hasMockData && (
            <>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    type="button"
                    variant="ghost"
                    size="xxs"
                    className="h-7 px-2 text-low-emphasis"
                    aria-label="Edit mock data"
                    onClick={handleSetMockDataClick}
                  >
                    <Pencil className="h-4 w-4" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent className="text-xs">Edit mock data</TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button
                    type="button"
                    variant="ghost"
                    size="xxs"
                    className="h-7 px-2 text-low-emphasis"
                    aria-label="Remove mock data"
                    onClick={handleRemoveMockData}
                  >
                    <PinOff className="h-4 w-4" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent className="text-xs">Remove mock data</TooltipContent>
              </Tooltip>
            </>
          )}
          {!isEditingMock && (
            <Tabs value={tab} onValueChange={setTab}>
              <TabsList>
                <TabsTrigger value="schema">Schema</TabsTrigger>
                <TabsTrigger value="table">Table</TabsTrigger>
                <TabsTrigger value="json">JSON</TabsTrigger>
              </TabsList>
            </Tabs>
          )}
          {onToggleCollapse && (
            <button
              onClick={onToggleCollapse}
              aria-label={isCollapsed ? "Expand output" : "Collapse output"}
              className="text-low-emphasis hover:bg-surface-hover rounded p-1 transition-colors"
            >
              {isCollapsed ? <ChevronDown size={18} /> : <ChevronUp size={18} />}
            </button>
          )}
        </div>
      </div>

      {failedRunError && !isEditingMock && (
        <div
          role="alert"
          className="mt-2 flex max-h-32 shrink-0 gap-2 overflow-y-auto rounded border border-error/40 bg-error/5 p-2 text-xs text-error"
        >
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <p className="whitespace-pre-wrap break-words">{failedRunError}</p>
        </div>
      )}

      {splitOutSummary && !isEditingMock && (
        <div className="mt-2 flex shrink-0 flex-col gap-1.5">
          <p className="text-xs text-medium-emphasis">
            {countItems(splitOutSummary.inputCount)} in → {countItems(splitOutSummary.outputCount)} out
          </p>
          {splitOutSummary.notFound.map((field) => (
            <output key={field} className="flex gap-2 rounded border border-warning-200 bg-warning-50 p-2 text-xs text-warning-800">
              <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span className="break-words">The field &apos;{field}&apos; wasn&apos;t found in any input item</span>
            </output>
          ))}
        </div>
      )}

      {limitSummary && !isEditingMock && (
        <div className="mt-2 flex shrink-0 flex-col gap-1.5">
          <p className="text-xs text-medium-emphasis">
            {countItems(limitSummary.inputCount)} in → {countItems(limitSummary.outputCount)} out
          </p>
        </div>
      )}

      {mergeSummary && !isEditingMock && (
        <div className="mt-2 flex shrink-0 flex-col gap-1.5">
          <p className="text-xs text-medium-emphasis">
            {mergeSummary.inputs.map(({ label, count }) => `${label} · ${countItems(count)}`).join("   ")}
          </p>
          {mergeSummary.withoutData.length > 0 && (
            <output className="flex gap-2 rounded border border-warning-200 bg-warning-50 p-2 text-xs text-warning-800">
              <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span className="break-words">
                Run When is All: {mergeSummary.withoutData.join(", ")} brought no items, so nothing was output.
              </span>
            </output>
          )}
        </div>
      )}

      {isEditingMock ? (
        <div className="mt-2 flex flex-1 flex-col gap-2 overflow-hidden rounded bg-surface-app p-2">
          <p className="text-[11px] text-low-emphasis">
            Data from last successful execution is inserted here. You can edit it below.
          </p>
          <Textarea
            className="flex-1 resize-none font-mono text-xs"
            value={mockDataInput}
            onChange={(e) => setMockDataInput(e.target.value)}
          />
          <div className="flex justify-end gap-2">
            <Button variant="outline" size="sm" onClick={handleCancelMockData}>
              Cancel
            </Button>
            <Button size="sm" onClick={handleSaveMockData}>
              Save
            </Button>
          </div>
        </div>
      ) : (
        <>
          {!isCollapsed && tab === "schema" && (
            <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {runtimeOutputRows.length === 0 ? (
            <p className="text-xs text-low-emphasis">No runtime output schema available.</p>
          ) : (
            <div className="flex flex-col gap-3">
              {runtimeOutputByBranch.map((group) => (
                <div key={group.key} className="flex flex-col gap-2">
                  {hasMultipleBranches && <BranchHeader group={group} className="px-1" />}
                  {hasMultipleBranches && group.rows.length === 0 && (
                    <p className="rounded border border-dashed border-border/60 px-2 py-1.5 text-xs text-low-emphasis">No items went to this output.</p>
                  )}
                  {group.rows.map((row, index) => (
                    <div
                      key={`${group.key}-${index}`}
                      className="rounded border border-border/60 p-2"
                    >
                      <p className="mb-1 text-xs font-semibold text-medium-emphasis">
                        item {index + 1}:
                      </p>
                      {isRecord(row) && group.schema.length > 0 ? (
                        <div className="flex flex-col gap-1">
                          {group.schema.map((field) => (
                            <div
                              key={`${group.key}-${index}-${field.key}`}
                              className="hover:bg-surface-hover group flex min-w-0 items-start gap-2 rounded px-2 py-1 text-xs"
                            >
                              <span className="shrink-0 py-0.5 font-mono text-high-emphasis">{field.key}:</span>
                              <span className="min-w-0 flex-1 whitespace-pre-wrap py-0.5 text-low-emphasis [overflow-wrap:anywhere]">
                                {formatCellValue(row[field.key])}
                              </span>
                              <CopyValueButton
                                value={toCopyText(row[field.key])}
                                label={`Copy value of ${field.key}`}
                                className="-my-0.5 h-5 px-1 opacity-0 focus-visible:opacity-100 group-hover:opacity-100"
                                iconClassName="h-3.5 w-3.5"
                              />
                            </div>
                          ))}
                        </div>
                      ) : (
                        <p className="whitespace-pre-wrap px-2 py-1 text-xs text-low-emphasis [overflow-wrap:anywhere]">
                          {formatCellValue(row)}
                        </p>
                      )}
                    </div>
                  ))}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {!isCollapsed && tab === "table" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {hasMultipleBranches ? (
            <div className="flex flex-col gap-3">
              {runtimeOutputByBranch.map((group) => (
                <div key={group.key} className="rounded border border-border/60 p-2">
                  <BranchHeader group={group} className="mb-2" />
                  {group.rows.length === 0 ? (
                    <p className="text-xs text-low-emphasis">No items went to this output.</p>
                  ) : (
                    <RuntimeTable rows={group.rows} />
                  )}
                </div>
              ))}
            </div>
          ) : (
            <RuntimeTable rows={runtimeOutputRows} />
          )}
        </div>
      )}

      {!isCollapsed && tab === "json" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app">
          {hasMultipleBranches ? (
            <div className="flex flex-col gap-3 p-2">
              {runtimeOutputByBranch.map((group) => (
                <div key={group.key} className="rounded border border-border/60">
                  <BranchHeader group={group} className="border-b border-border/60 px-3 py-2" />
                  {group.rows.length === 0 ? (
                    <p className="px-3 py-2 text-xs text-low-emphasis">No items went to this output.</p>
                  ) : (
                    <RuntimeJson rows={group.rows} />
                  )}
                </div>
              ))}
            </div>
          ) : (
            <RuntimeJson rows={runtimeOutputRows} />
          )}
        </div>
      )}
        </>
      )}
    </div>
  );
};

function RuntimeTable({ rows }: { rows: unknown[] }) {
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime output data available.</p>;
  }

  const primitiveColumn = "(value)";
  const hasPrimitiveRows = rows.some((row) => !isRecord(row));
  const objectColumns = Array.from(
    rows.reduce<Set<string>>((acc, row) => {
      if (!isRecord(row)) return acc;
      Object.keys(row).forEach((key) => acc.add(key));
      return acc;
    }, new Set<string>()),
  );

  const columns: string[] = hasPrimitiveRows ? [primitiveColumn, ...objectColumns] : objectColumns;

  return (
    <ScrollArea className="h-full w-full whitespace-nowrap rounded">
      <table className="min-w-max border-separate border-spacing-0 text-xs">
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column}
                className="border-b border-border px-2 py-1 text-left font-semibold text-medium-emphasis"
              >
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              {columns.map((column) => (
                <td
                  key={column}
                  className="max-w-xs whitespace-normal border-b border-border/50 px-2 py-1 align-top text-high-emphasis [overflow-wrap:anywhere]"
                >
                  {column === primitiveColumn
                    ? !isRecord(row)
                      ? formatCellValue(row)
                      : ""
                    : isRecord(row)
                      ? formatCellValue(row[column])
                      : ""}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <ScrollBar orientation="horizontal" />
    </ScrollArea>
  );
}

function RuntimeJson({ rows }: { rows: unknown[] }) {
  if (rows.length === 0) {
    return <p className="p-3 text-xs text-low-emphasis">No runtime output data available.</p>;
  }

  const json = JSON.stringify(rows, null, 2);

  return (
    <div className="relative">
      <CopyValueButton value={json} label="Copy output JSON" className="absolute right-2 top-2" />
      <pre className="whitespace-pre-wrap p-3 pr-12 text-xs leading-relaxed text-high-emphasis [overflow-wrap:anywhere]">
        <code>{json}</code>
      </pre>
    </div>
  );
}

/**
 * Error text of the node's latest run, or null when that run did not fail. Runs saved before the server
 * stored a user-facing message hold ex.ToString(): cut it before the stack trace and drop the
 * "System.Exception: " prefix.
 */
const countItems = (count: number) => `${count} ${count === 1 ? "item" : "items"}`;

function latestRun(executedNodes: ExecutedNode[] | null | undefined, nodeId: string): ExecutedNode | null {
  return (executedNodes ?? [])
    .filter((ne) => ne.nodeId === nodeId)
    .reduce<ExecutedNode | null>((last, ne) => (!last || ne.runIndex >= last.runIndex ? ne : last), null);
}

function latestRunError(executedNodes: ExecutedNode[] | null | undefined, nodeId: string): string | null {
  const latest = latestRun(executedNodes, nodeId);
  if (!latest || latest.status !== NodeExecutionStatus.Failed) return null;

  const message = (latest.error ?? "").split(/\r?\n\s+at /)[0].replace(/^System\.Exception:\s*/, "").trim();
  return message || "This step failed.";
}

function formatCellValue(value: unknown): string {
  if (value === null || value === undefined) return String(value);
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);

  try {
    return JSON.stringify(value);
  } catch {
    return "[unserializable]";
  }
}

function toCopyText(value: unknown): string {
  return typeof value === "string" ? value : formatCellValue(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}
