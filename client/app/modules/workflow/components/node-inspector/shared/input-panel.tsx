import { useCallback, useMemo, useState } from "react";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useWorkflowStore } from "@blocks-workflow/store/workflow-store";
import { copyToClipboard } from "@blocks-workflow/utils/copy-to-clipboard";
import { inferSchemaFromRuntimeRows } from "@blocks-workflow/utils/runtime-node-data";

export const InputPanel = () => {
  const [tab, setTab] = useState("schema");
  const selectedNode = useWorkflowStore((s) => s.selectedNode);
  const executedNodes = useWorkflowStore((s) => s.executedNodes);

  const runtimeInputRows = useMemo(() => {
    if (!selectedNode) return [];
    return executedNodes.find((en) => en.nodeId === selectedNode.id)?.input || [];
  }, [executedNodes, selectedNode]);

  const inputSchema = useMemo(
    () => inferSchemaFromRuntimeRows(runtimeInputRows),
    [runtimeInputRows],
  );

  const handleCopy = useCallback(
    (fieldKey: string) => {
      if (!selectedNode) return;
      copyToClipboard(`{{node_${selectedNode.id}_${selectedNode.category}.${fieldKey}}}`);
    },
    [selectedNode],
  );

  if (!selectedNode) return null;

  return (
    <div className="flex h-full w-full flex-col overflow-hidden">
      <div className="flex items-center justify-between">
        <h3 className="font-medium text-high-emphasis">Input</h3>
        <Tabs value={tab} onValueChange={setTab}>
          <TabsList>
            <TabsTrigger value="schema">Schema</TabsTrigger>
            <TabsTrigger value="table">Table</TabsTrigger>
            <TabsTrigger value="json">JSON</TabsTrigger>
          </TabsList>
        </Tabs>
      </div>

      {tab === "schema" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {runtimeInputRows.length === 0 ? (
            <p className="text-xs text-low-emphasis">No runtime input schema available.</p>
          ) : (
            <div className="flex flex-col gap-3">
              {runtimeInputRows.map((row, index) => (
                <div key={index} className="rounded border border-border/60 p-2">
                  <p className="mb-1 text-xs font-semibold text-medium-emphasis">
                    item {index + 1}:
                  </p>
                  {isRecord(row) && inputSchema.length > 0 ? (
                    <div className="flex flex-col gap-1">
                      {inputSchema.map((field) => (
                        <div
                          key={`${index}-${field.key}`}
                          className="hover:bg-surface-hover group flex cursor-pointer items-center gap-2 rounded px-2 py-1 text-xs"
                          onClick={(e) => {
                            e.stopPropagation();
                            handleCopy(field.key);
                          }}
                          title={`Click to copy expression for ${field.key}`}
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
                    <p className="px-2 py-1 text-xs text-low-emphasis">{formatCellValue(row)}</p>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {tab === "table" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          <RuntimeTable rows={runtimeInputRows} />
        </div>
      )}

      {tab === "json" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app">
          <RuntimeJson rows={runtimeInputRows} />
        </div>
      )}
    </div>
  );
};

function RuntimeTable({ rows }: { rows: unknown[] }) {
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">No runtime input data available.</p>;
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
    return <p className="p-3 text-xs text-low-emphasis">No runtime input data available.</p>;
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
