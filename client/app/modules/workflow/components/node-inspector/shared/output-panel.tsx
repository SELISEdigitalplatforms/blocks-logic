import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { useWorkflowStore } from "@blocks-workflow/store";
import { copyToClipboard } from "@blocks-workflow/utils/copy-to-clipboard";
import { useCallback, useMemo, useState } from "react";
import { inferSchemaFromRuntimeRows } from "@blocks-workflow/utils/runtime-node-data";
import { ChevronDown, ChevronUp } from "lucide-react";

type BranchGroup = {
  branch: string;
  rows: unknown[];
  schema: ReturnType<typeof inferSchemaFromRuntimeRows>;
};

export const OutputPanel = ({
  isCollapsed,
  onToggleCollapse,
}: {
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
} = {}) => {
  const [tab, setTab] = useState("schema");
  const selectedNode = useWorkflowStore((s) => s.selectedNode);
  const executedItems = useWorkflowStore((s) => s.executedItems);

  const runtimeOutputByBranch = useMemo<BranchGroup[]>(() => {
    if (!selectedNode) return [];

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

    return Array.from(groupedRows.entries()).map(([branch, rows]) => ({
      branch,
      rows,
      schema: inferSchemaFromRuntimeRows(rows),
    }));
  }, [executedItems, selectedNode]);

  const runtimeOutputRows = useMemo(
    () => runtimeOutputByBranch.flatMap((group) => group.rows),
    [runtimeOutputByBranch],
  );

  const hasMultipleBranches = runtimeOutputByBranch.length > 1;

  const handleCopyExpression = useCallback((value: unknown) => {
    const stringValue = typeof value === "string" ? value : JSON.stringify(value);
    copyToClipboard(stringValue);
  }, []);

  if (!selectedNode) return null;

  return (
    <div className={`flex w-full flex-col overflow-hidden ${isCollapsed ? 'h-fit shrink-0' : 'h-full flex-1'}`}>
      <div className="flex items-center justify-between">
        <h3 className="font-medium text-high-emphasis">Output</h3>
        <div className="flex items-center gap-2">
          <Tabs value={tab} onValueChange={setTab}>
            <TabsList>
              <TabsTrigger value="schema">Schema</TabsTrigger>
              <TabsTrigger value="table">Table</TabsTrigger>
              <TabsTrigger value="json">JSON</TabsTrigger>
            </TabsList>
          </Tabs>
          {onToggleCollapse && (
            <button
              onClick={onToggleCollapse}
              className="text-low-emphasis hover:bg-surface-hover rounded p-1 transition-colors"
            >
              {isCollapsed ? <ChevronDown size={18} /> : <ChevronUp size={18} />}
            </button>
          )}
        </div>
      </div>

      {!isCollapsed && tab === "schema" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {runtimeOutputRows.length === 0 ? (
            <p className="text-xs text-low-emphasis">No runtime output schema available.</p>
          ) : (
            <div className="flex flex-col gap-3">
              {runtimeOutputByBranch.map((group) => (
                <div key={group.branch} className="flex flex-col gap-2">
                  {hasMultipleBranches && (
                    <p className="px-1 text-xs font-semibold uppercase tracking-wide text-medium-emphasis">
                      Branch: {group.branch}
                    </p>
                  )}
                  {group.rows.map((row, index) => (
                    <div
                      key={`${group.branch}-${index}`}
                      className="rounded border border-border/60 p-2"
                    >
                      <p className="mb-1 text-xs font-semibold text-medium-emphasis">
                        item {index + 1}:
                      </p>
                      {isRecord(row) && group.schema.length > 0 ? (
                        <div className="flex flex-col gap-1">
                          {group.schema.map((field) => (
                            <div
                              key={`${group.branch}-${index}-${field.key}`}
                              className="hover:bg-surface-hover group flex cursor-pointer items-center gap-2 rounded px-2 py-1 text-xs"
                              onClick={(e) => {
                                e.stopPropagation();
                                handleCopyExpression(row[field.key]);
                              }}
                              title={`Click to copy value for ${field.key}`}
                            >
                              <span className="font-mono text-high-emphasis">{field.key}:</span>
                              <span className="text-low-emphasis">
                                {formatCellValue(row[field.key])}
                              </span>
                              <span className="ml-auto hidden text-[10px] text-low-emphasis group-hover:inline">
                                click to copy
                              </span>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <p className="px-2 py-1 text-xs text-low-emphasis">
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
                <div key={group.branch} className="rounded border border-border/60 p-2">
                  <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-medium-emphasis">
                    Branch: {group.branch}
                  </p>
                  <RuntimeTable rows={group.rows} />
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
                <div key={group.branch} className="rounded border border-border/60">
                  <p className="border-b border-border/60 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-medium-emphasis">
                    Branch: {group.branch}
                  </p>
                  <RuntimeJson rows={group.rows} />
                </div>
              ))}
            </div>
          ) : (
            <RuntimeJson rows={runtimeOutputRows} />
          )}
        </div>
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
                <td key={column} className="border-b border-border/50 px-2 py-1 text-high-emphasis">
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

  return (
    <pre className="p-3 text-xs leading-relaxed text-high-emphasis">
      <code>{JSON.stringify(rows, null, 2)}</code>
    </pre>
  );
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

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}
