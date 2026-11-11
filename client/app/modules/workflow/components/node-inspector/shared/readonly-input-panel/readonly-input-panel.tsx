import { useMemo, useState } from "react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { ScrollArea, ScrollBar } from "@/components/ui-kits/scroll-area/scroll-area";
import { ChevronDown, ChevronUp } from "lucide-react";
import { useWorkflowStore } from "@blocks-workflow/store";
import { inferSchemaFromRuntimeRows } from "@blocks-workflow/utils/runtime-node-data";
import type { ExecutedNode } from "@blocks-workflow/models/workflow.model";
import { CopyValueButton } from "../copy-value-button";
import { useT } from "@seliseblocks/genesis-os";

const LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE = "logic.workflow.nodes.inspector";

/**
 * The Input panel outside the workflow editor (execution and version views). It shows the selected
 * node's own recorded input from its latest run, laid out like OutputPanel, with no drag handles and
 * no mock data. The rendering mirrors output-panel.tsx on purpose so the two panels look the same.
 */
export const ReadonlyInputPanel = ({
  isCollapsed,
  onToggleCollapse,
}: {
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
} = {}) => {
  const { t } = useT(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE);
  const [tab, setTab] = useState("schema");
  const selectedNode = useWorkflowStore((s) => s.selectedNode);
  const executedNodes = useWorkflowStore((s) => s.executedNodes);

  const rows = useMemo<unknown[]>(() => {
    if (!selectedNode) return [];
    const run = latestRun(executedNodes, selectedNode.id);
    return Array.isArray(run?.input) ? run.input : [];
  }, [executedNodes, selectedNode]);

  const schema = useMemo(() => inferSchemaFromRuntimeRows(rows), [rows]);

  if (!selectedNode) return null;

  return (
    <div className={`flex w-full flex-col overflow-hidden ${isCollapsed ? "h-fit shrink-0" : "h-full flex-1"}`}>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="font-medium text-high-emphasis">{t("INPUT_PANEL.INPUT")}</h3>
        </div>
        <div className="flex items-center gap-2">
          <Tabs value={tab} onValueChange={setTab}>
            <TabsList>
              <TabsTrigger value="schema">{t("INPUT_PANEL.SCHEMA")}</TabsTrigger>
              <TabsTrigger value="table">{t("INPUT_PANEL.TABLE")}</TabsTrigger>
              <TabsTrigger value="json">JSON</TabsTrigger>
            </TabsList>
          </Tabs>
          {onToggleCollapse && (
            <button
              onClick={onToggleCollapse}
              aria-label={isCollapsed ? t("INPUT_PANEL.EXPAND_INPUT") : t("INPUT_PANEL.COLLAPSE_INPUT")}
              className="text-low-emphasis hover:bg-surface-hover rounded p-1 transition-colors"
            >
              {isCollapsed ? <ChevronDown size={18} /> : <ChevronUp size={18} />}
            </button>
          )}
        </div>
      </div>

      {!isCollapsed && tab === "schema" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          {rows.length === 0 ? (
            <p className="text-xs text-low-emphasis">{t("SCHEMA_TAB.NO_RUNTIME_INPUT_SCHEMA")}</p>
          ) : (
            <div className="flex flex-col gap-2">
              {rows.map((row, index) => (
                <div key={index} className="rounded border border-border/60 p-2">
                  <p className="mb-1 text-xs font-semibold text-medium-emphasis">
                    {t("SCHEMA_TAB.ITEM", { value: index + 1 })}
                  </p>
                  {isRecord(row) && schema.length > 0 ? (
                    <div className="flex flex-col gap-1">
                      {schema.map((field) => (
                        <div
                          key={`${index}-${field.key}`}
                          className="hover:bg-surface-hover group flex min-w-0 items-start gap-2 rounded px-2 py-1 text-xs"
                        >
                          <span className="shrink-0 py-0.5 font-mono text-high-emphasis">{field.key}:</span>
                          <span className="min-w-0 flex-1 whitespace-pre-wrap py-0.5 text-low-emphasis [overflow-wrap:anywhere]">
                            {formatCellValue(row[field.key])}
                          </span>
                          <CopyValueButton
                            value={toCopyText(row[field.key])}
                            label={t("OUTPUT_PANEL.COPY_VALUE_OF", { key: field.key })}
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
          )}
        </div>
      )}

      {!isCollapsed && tab === "table" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app p-2">
          <RuntimeTable rows={rows} />
        </div>
      )}

      {!isCollapsed && tab === "json" && (
        <div className="mt-2 flex-1 overflow-y-auto rounded bg-surface-app">
          <RuntimeJson rows={rows} />
        </div>
      )}
    </div>
  );
};

function RuntimeTable({ rows }: Readonly<{ rows: unknown[] }>) {
  const { t } = useT(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE);
  if (rows.length === 0) {
    return <p className="text-xs text-low-emphasis">{t("TABLE_TAB.NO_RUNTIME_INPUT_DATA")}</p>;
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
                  {cellText(row, column, primitiveColumn)}
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

function RuntimeJson({ rows }: Readonly<{ rows: unknown[] }>) {
  const { t } = useT(LOGIC_WORKFLOW_NODES_INSPECTOR_MODULE);
  if (rows.length === 0) {
    return <p className="p-3 text-xs text-low-emphasis">{t("JSON_TAB.NO_RUNTIME_INPUT_DATA")}</p>;
  }

  const json = JSON.stringify(rows, null, 2);

  return (
    <div className="relative">
      <CopyValueButton value={json} label={t("JSON_TAB.COPY_INPUT_JSON")} className="absolute right-2 top-2" />
      <pre className="whitespace-pre-wrap p-3 pr-12 text-xs leading-relaxed text-high-emphasis [overflow-wrap:anywhere]">
        <code>{json}</code>
      </pre>
    </div>
  );
}

/** The node's run with the highest runIndex; on a tie the later array entry wins. */
function latestRun(executedNodes: ExecutedNode[] | null | undefined, nodeId: string): ExecutedNode | null {
  return (executedNodes ?? [])
    .filter((ne) => ne.nodeId === nodeId)
    .reduce<ExecutedNode | null>((last, ne) => (!last || ne.runIndex >= last.runIndex ? ne : last), null);
}

function cellText(row: unknown, column: string, primitiveColumn: string): string {
  if (column === primitiveColumn) return isRecord(row) ? "" : formatCellValue(row);
  return isRecord(row) ? formatCellValue(row[column]) : "";
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
